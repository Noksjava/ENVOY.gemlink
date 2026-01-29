using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GenerativeAI;
using GenerativeAI.Live;

namespace SipAiGateway;

public delegate int ReadPcmHandler(Span<short> destination);

public sealed class GeminiLiveClient : IDisposable
{
    private readonly ReadPcmHandler _readPcm;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly string _systemPrompt;
    private string _selectedVoiceName = GeminiVoiceCatalog.DefaultVoice;
    private readonly Action<byte[]>? _onPcmuFrame;
    private readonly CancellationTokenSource _cts = new();
    private Task? _task;
    private bool _running;
    private MultiModalLiveClient? _liveClient;
    private volatile bool _connected;
    private string _resolvedModelName = string.Empty;
    private volatile bool _sentGreeting;
    private readonly object _clientLock = new();
    private string _resolvedApiKey = string.Empty;
    private volatile bool _connecting;
    private volatile bool _needsReset;
    private readonly List<short> _geminiAudioBuffer = new();
    private readonly object _audioLock = new();

    private readonly short[] _frame8k = new short[Config.SamplesPerFrame];
    private readonly short[] _frame16k = new short[Config.GeminiInputSamplesPerFrame];
    private readonly short[] _frame8kFromGemini = new short[Config.SamplesPerFrame];
    private readonly byte[] _pcmBytes = new byte[Config.GeminiInputSamplesPerFrame * sizeof(short)];
    private readonly EventHandler? _onConnected;
    private readonly EventHandler? _onDisconnected;
    private readonly EventHandler<ErrorEventArgs>? _onError;
    private Delegate? _onAudioChunk;

    public GeminiLiveClient(
        ReadPcmHandler readPcm,
        string endpoint,
        string apiKey,
        string modelName,
        string systemPrompt,
        string voiceName,
        Action<byte[]>? onPcmuFrame)
    {
        _readPcm = readPcm ?? throw new ArgumentNullException(nameof(readPcm));
        _endpoint = endpoint ?? string.Empty;
        _apiKey = apiKey ?? string.Empty;
        _modelName = modelName ?? string.Empty;
        _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? AppSettings.DefaultGeminiSystemPrompt
            : systemPrompt.Trim();
        _selectedVoiceName = GeminiVoiceCatalog.GetVoice(voiceName).Name;
        _onPcmuFrame = onPcmuFrame;
        _onConnected = (_, _) =>
        {
            _connected = true;
            _connecting = false;
            _needsReset = false;
            _sentGreeting = false;
            Console.WriteLine("Gemini Live connected.");
        };
        _onDisconnected = (_, _) =>
        {
            _connected = false;
            _connecting = false;
            _needsReset = true;
            Console.WriteLine("Gemini Live disconnected.");
        };
        _onError = (_, e) =>
        {
            var exception = e.GetException();
            _needsReset = true;
            if (exception == null)
            {
                Console.WriteLine("Gemini Live error: (no exception details)");
                return;
            }

            LogGeminiException("Gemini Live error", exception);
        };
        _onAudioChunk = null;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _task = Task.Run(StreamLoop);
    }

    public void SetVoice(string voiceName)
    {
        var normalized = GeminiVoiceCatalog.GetVoice(voiceName).Name;
        if (_selectedVoiceName == normalized)
        {
            return;
        }

        _selectedVoiceName = normalized;
        _needsReset = true;
    }

    private async Task StreamLoop()
    {
        var ct = _cts.Token;
        int filled = 0;
        long lastLog = Environment.TickCount64;
        int sentFrames = 0;
        long nextReconnectAttempt = 0;
        int reconnectDelayMs = 2000;
        const int maxReconnectDelayMs = 30000;
        var wasConnected = false;

        if (!InitializeSdk())
        {
            Console.WriteLine("Gemini Live disabled: failed to initialize Google_GenerativeAI SDK.");
            return;
        }

        await ConnectLiveClientAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(_endpoint))
        {
            Console.WriteLine("Gemini Live endpoint not set; SDK initialized for future live audio streaming.");
        }

        Console.WriteLine($"Gemini Live client started. Model={_resolvedModelName}");

        while (!ct.IsCancellationRequested)
        {
            if (_needsReset)
            {
                ResetLiveClient();
                reconnectDelayMs = Math.Min(reconnectDelayMs * 2, maxReconnectDelayMs);
                nextReconnectAttempt = Environment.TickCount64 + reconnectDelayMs;
                await Task.Delay(500, ct).ConfigureAwait(false);
                continue;
            }

            if (_connected)
            {
                if (!wasConnected)
                {
                    reconnectDelayMs = 2000;
                    nextReconnectAttempt = Environment.TickCount64 + reconnectDelayMs;
                }
                wasConnected = true;
            }
            else
            {
                wasConnected = false;
            }

            if (!_connected && !_connecting && Environment.TickCount64 >= nextReconnectAttempt)
            {
                nextReconnectAttempt = Environment.TickCount64 + reconnectDelayMs;
                if (_liveClient == null && !InitializeSdk())
                {
                    Console.WriteLine("Gemini Live disabled: failed to initialize Google_GenerativeAI SDK.");
                    return;
                }
                await ConnectLiveClientAsync(ct).ConfigureAwait(false);
                if (!_connected)
                {
                    reconnectDelayMs = Math.Min(reconnectDelayMs * 2, maxReconnectDelayMs);
                }
            }
            else if (_connected && !_sentGreeting)
            {
                await SendGreetingAsync().ConfigureAwait(false);
            }

            if (filled < _frame8k.Length)
            {
                var read = _readPcm(_frame8k.AsSpan(filled));
                if (read > 0)
                {
                    filled += read;
                }
                else
                {
                    await Task.Delay(5, ct).ConfigureAwait(false);
                }
            }

            if (filled < _frame8k.Length)
            {
                continue;
            }

            AudioResampler.Upsample2x(_frame8k, _frame16k);
            Buffer.BlockCopy(_frame16k, 0, _pcmBytes, 0, _pcmBytes.Length);
            await SendPcmFrameAsync(_pcmBytes).ConfigureAwait(false);
            filled = 0;
            sentFrames++;

            var now = Environment.TickCount64;
            if (now - lastLog >= 1000)
            {
                lastLog = now;
                Console.WriteLine($"Gemini Live tx audio frames={sentFrames}");
            }
        }
    }

    private bool InitializeSdk()
    {
        try
        {
            var resolvedApiKey = string.IsNullOrWhiteSpace(_apiKey)
                ? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                : _apiKey;

            if (string.IsNullOrWhiteSpace(resolvedApiKey))
            {
                Console.WriteLine("Gemini Live: API key not set; falling back to environment variables.");
            }
            else
            {
                Environment.SetEnvironmentVariable("GOOGLE_API_KEY", resolvedApiKey);
            }

            var modelName = GeminiModelCatalog.NormalizeForLive(_modelName);
            _resolvedModelName = modelName;

            if (string.IsNullOrWhiteSpace(resolvedApiKey))
            {
                throw new InvalidOperationException("Gemini Live API key is required.");
            }

            _resolvedApiKey = resolvedApiKey;
            if (_liveClient == null)
            {
                _connected = false;
                _connecting = false;
                _sentGreeting = false;
            }
            CreateLiveClient();
            return true;
        }
        catch (Exception ex)
        {
            if (ex is ObjectDisposedException)
            {
                _connected = false;
                _needsReset = true;
            }
            Console.WriteLine($"Gemini Live audio send error: {ex.Message}");
            return false;
        }
    }

    private void CreateLiveClient()
    {
        lock (_clientLock)
        {
            if (_liveClient != null || string.IsNullOrWhiteSpace(_resolvedApiKey))
            {
                return;
            }

            var generationConfig = new GenerativeAI.Types.GenerationConfig
            {
                ResponseModalities = new List<GenerativeAI.Types.Modality>
                {
                    GenerativeAI.Types.Modality.AUDIO
                },
                SpeechConfig = new GenerativeAI.Types.SpeechConfig
                {
                    VoiceConfig = new GenerativeAI.Types.VoiceConfig
                    {
                        PrebuiltVoiceConfig = new GenerativeAI.Types.PrebuiltVoiceConfig
                        {
                            VoiceName = _selectedVoiceName
                        }
                    }
                }
            };

            _liveClient = new MultiModalLiveClient(
                platformAdapter: new GoogleAIPlatformAdapter(_resolvedApiKey),
                modelName: _resolvedModelName,
                generationConfig,
                systemInstruction: _systemPrompt
            );
            _liveClient.Connected += _onConnected;
            _liveClient.Disconnected += _onDisconnected;
            _liveClient.ErrorOccurred += _onError;

            var audioEvent = _liveClient.GetType().GetEvent("AudioChunkReceived");
            if (audioEvent != null)
            {
                _onAudioChunk = CreateAudioChunkDelegate(audioEvent);
                if (_onAudioChunk != null)
                {
                    audioEvent.AddEventHandler(_liveClient, _onAudioChunk);
                }
            }
        }
    }

    private void ResetLiveClient()
    {
        lock (_clientLock)
        {
            _connected = false;
            _sentGreeting = false;
            _connecting = false;
            _needsReset = false;

            if (_liveClient != null)
            {
                try
                {
                    if (_onConnected != null)
                    {
                        _liveClient.Connected -= _onConnected;
                    }

                    if (_onDisconnected != null)
                    {
                        _liveClient.Disconnected -= _onDisconnected;
                    }

                    if (_onError != null)
                    {
                        _liveClient.ErrorOccurred -= _onError;
                    }

                    if (_onAudioChunk != null)
                    {
                        var audioEvent = _liveClient.GetType().GetEvent("AudioChunkReceived");
                        audioEvent?.RemoveEventHandler(_liveClient, _onAudioChunk);
                        _onAudioChunk = null;
                    }

                    _liveClient.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Gemini Live: error disposing client: {ex.Message}");
                }
                _liveClient = null;
            }
        }
        Console.WriteLine("Gemini Live: Client reset performed.");
    }

    private async Task SendPcmFrameAsync(byte[] pcmBytes)
    {
        var client = _liveClient;
        if (client == null || !_connected || _needsReset)
        {
            return;
        }

        try
        {
            await client.SendAudioAsync(pcmBytes)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            _needsReset = true;
            Console.WriteLine("Gemini Live send warning: socket disposed while sending.");
        }
        catch (WebSocketException ex)
        {
            _needsReset = true;
            Console.WriteLine($"Gemini Live send warning: WebSocket error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Gemini Live send error: {ex.Message}");
        }
    }

    private async Task SendGreetingAsync()
    {
        MultiModalLiveClient? liveClient;
        lock (_clientLock)
        {
            liveClient = _liveClient;
        }

        if (liveClient == null || !_connected || _sentGreeting || _needsReset)
        {
            return;
        }

        try
        {
            await liveClient.SentTextAsync("Hello. You are an assistant, pretend you're being called on the phone.")
                .ConfigureAwait(false);
            _sentGreeting = true;
        }
        catch (Exception ex)
        {
            if (ex is ObjectDisposedException)
            {
                ResetLiveClient();
            }
            Console.WriteLine($"Gemini Live greeting send error: {ex.Message}");
        }
    }

    public void HandleGeminiAudio(ReadOnlySpan<short> pcm16k)
    {
        if (pcm16k.Length < Config.GeminiOutputSamplesPerFrame)
        {
            return;
        }

        AudioResampler.DownsampleBy3(pcm16k[..Config.GeminiOutputSamplesPerFrame], _frame8kFromGemini);
        var pcmuFrame = new byte[Config.PayloadBytes];
        for (int i = 0; i < Config.PayloadBytes; i++)
        {
            pcmuFrame[i] = G711.Pcm16ToMuLaw(_frame8kFromGemini[i]);
        }

        _onPcmuFrame?.Invoke(pcmuFrame);
    }

    private void HandleGeminiAudioBytes(byte[] buffer)
    {
        if (buffer.Length < sizeof(short))
        {
            return;
        }

        var samples = new short[buffer.Length / sizeof(short)];
        Buffer.BlockCopy(buffer, 0, samples, 0, samples.Length * sizeof(short));

        lock (_audioLock)
        {
            _geminiAudioBuffer.AddRange(samples);
            while (_geminiAudioBuffer.Count >= Config.GeminiOutputSamplesPerFrame)
            {
                var frame = _geminiAudioBuffer.GetRange(0, Config.GeminiOutputSamplesPerFrame);
                _geminiAudioBuffer.RemoveRange(0, Config.GeminiOutputSamplesPerFrame);
                HandleGeminiAudio(frame.ToArray());
            }
        }
    }

    private void HandleAudioChunkEvent(object? sender, object? args)
    {
        if (args == null)
        {
            return;
        }

        var bufferProperty = args.GetType().GetProperty("Buffer");
        if (bufferProperty?.GetValue(args) is byte[] buffer)
        {
            HandleGeminiAudioBytes(buffer);
        }
    }

    private Delegate? CreateAudioChunkDelegate(EventInfo audioEvent)
    {
        var handlerType = audioEvent.EventHandlerType;
        if (handlerType == null)
        {
            return null;
        }

        var invokeMethod = handlerType.GetMethod("Invoke");
        if (invokeMethod == null)
        {
            return null;
        }

        var parameters = invokeMethod.GetParameters();
        if (parameters.Length != 2)
        {
            return null;
        }

        var senderParam = Expression.Parameter(parameters[0].ParameterType, "sender");
        var argsParam = Expression.Parameter(parameters[1].ParameterType, "args");
        var call = Expression.Call(
            Expression.Constant(this),
            typeof(GeminiLiveClient).GetMethod(
                nameof(HandleAudioChunkEvent),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("Missing handler method."),
            Expression.Convert(senderParam, typeof(object)),
            Expression.Convert(argsParam, typeof(object)));

        return Expression.Lambda(handlerType, call, senderParam, argsParam).Compile();
    }

    private async Task ConnectLiveClientAsync(CancellationToken ct)
    {
        MultiModalLiveClient? liveClient;
        lock (_clientLock)
        {
            if (_liveClient == null || _connected || _connecting || _needsReset)
            {
                return;
            }
            _connecting = true;
            liveClient = _liveClient;
        }

        if (liveClient == null)
        {
            return;
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            await liveClient.ConnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _connecting = false;
            ResetLiveClient();
            Console.WriteLine($"Gemini Live connect error: {ex.Message}");
        }
    }

    private static void LogGeminiException(string prefix, Exception exception)
    {
        Console.WriteLine($"{prefix}: {exception.GetType().Name}: {exception.Message}");
        var seen = new HashSet<Exception>();
        var inner = exception.InnerException;
        while (inner != null && seen.Add(inner))
        {
            Console.WriteLine($"{prefix} inner: {inner.GetType().Name}: {inner.Message}");
            inner = inner.InnerException;
        }

        if (inner != null)
        {
            Console.WriteLine($"{prefix}: inner exception chain contains a cycle.");
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _task?.Wait(2000); } catch { }
        ResetLiveClient();
        _task = null;
        _running = false;
    }
}
