using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GenerativeAI;
using GenerativeAI.Live;
using Websocket.Client;

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
    private readonly EventHandler<ClientCreatedEventArgs>? _onClientCreated;
    private Delegate? _onAudioChunk;
    private readonly int _geminiInputFrameSamples;
    private readonly int _geminiOutputFrameSamples;
    private readonly bool _resamplerConfigurationValid;
    private readonly Channel<byte[]> _audioChunks;
    private Task? _audioChunkTask;
    private long _droppedAudioChunks;
    private long _resampleFailures;
    private readonly Channel<byte[]> _pcmuFrames;
    private Task? _pcmuFrameTask;
    private long _droppedPcmuFrames;
    private long _lastActivityMs;
    private long _lastSendMs;
    private const int IdleTimeoutMs = 150000;
    private const int KeepAliveIntervalMs = 20000;
    private readonly byte[] _keepAlivePcmBytes = new byte[Config.GeminiInputSamplesPerFrame * sizeof(short)];
    private readonly SemaphoreSlim _sendLock = new(1, 1);

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
            _lastActivityMs = Environment.TickCount64;
            _lastSendMs = _lastActivityMs;
            Console.WriteLine("Gemini Live connected.");
        };
        _onDisconnected = (_, _) =>
        {
            _connected = false;
            _connecting = false;
            _needsReset = true;
            _lastActivityMs = Environment.TickCount64;
            _lastSendMs = 0;
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
        _onClientCreated = (_, args) =>
        {
            ConfigureWebsocketClient(args.Client);
        };
        _onAudioChunk = null;
        _geminiInputFrameSamples = _frame16k.Length;
        _geminiOutputFrameSamples = _frame8kFromGemini.Length * 3;
        _resamplerConfigurationValid = ValidateResamplerBuffers();
        _audioChunks = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _pcmuFrames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _lastActivityMs = Environment.TickCount64;
        _lastSendMs = _lastActivityMs;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _task = Task.Run(StreamLoop);
        _audioChunkTask = Task.Run(ProcessAudioChunksAsync);
        _pcmuFrameTask = Task.Run(ProcessPcmuFramesAsync);
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
                var nowTick = Environment.TickCount64;
                var idleForMs = nowTick - Interlocked.Read(ref _lastActivityMs);

                if (idleForMs > IdleTimeoutMs)
                {
                    Console.WriteLine($"Gemini Live idle for {idleForMs}ms; resetting connection.");
                    _needsReset = true;
                    continue;
                }

                var sinceLastSendMs = nowTick - Interlocked.Read(ref _lastSendMs);
                if (sinceLastSendMs > KeepAliveIntervalMs)
                {
                    try
                    {
                        await SendPcmFrameAsync(_keepAlivePcmBytes).ConfigureAwait(false);
                        Interlocked.Exchange(ref _lastSendMs, nowTick);
                        Interlocked.Exchange(ref _lastActivityMs, nowTick);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Gemini Live keep-alive error: {ex.Message}");
                        _connected = false;
                        _needsReset = true;
                    }
                }
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

            if (!_resamplerConfigurationValid)
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
                continue;
            }

            if (!TryUpsampleFrame())
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                continue;
            }
            try
            {
                await SendPcmFrameAsync(_pcmBytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gemini Send Error: {ex.Message}");
                _connected = false;
                _needsReset = true;
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            filled = 0;
            sentFrames++;
            Interlocked.Exchange(ref _lastActivityMs, Environment.TickCount64);
            Interlocked.Exchange(ref _lastSendMs, Environment.TickCount64);

            var now = Environment.TickCount64;
            if (now - lastLog >= 1000)
            {
                lastLog = now;
                var dropped = Interlocked.Exchange(ref _droppedAudioChunks, 0);
                var droppedPcmu = Interlocked.Exchange(ref _droppedPcmuFrames, 0);
                Console.WriteLine(
                    dropped > 0 || droppedPcmu > 0
                        ? $"Gemini Live tx audio frames={sentFrames} (dropped audio chunks={dropped}, dropped pcmu frames={droppedPcmu})"
                        : $"Gemini Live tx audio frames={sentFrames}");
            }
        }
    }

    private async Task ProcessAudioChunksAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (await _audioChunks.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_audioChunks.Reader.TryRead(out var buffer))
                {
                    HandleGeminiAudioBytes(buffer);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task ProcessPcmuFramesAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (await _pcmuFrames.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_pcmuFrames.Reader.TryRead(out var frame))
                {
                    _onPcmuFrame?.Invoke(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
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
            if (_onClientCreated != null)
            {
                _liveClient.ClientCreated += _onClientCreated;
            }

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

    private static void ConfigureWebsocketClient(IWebsocketClient client)
    {
        if (client == null)
        {
            return;
        }

        client.ConnectTimeout = Timeout.InfiniteTimeSpan;
        client.IsReconnectionEnabled = false;
        client.ReconnectTimeout = null;
        client.ErrorReconnectTimeout = null;
    }

    private void ResetLiveClient()
    {
        lock (_clientLock)
        {
            _connected = false;
            _sentGreeting = false;
            _connecting = false;
            _needsReset = false;
            _lastSendMs = 0;

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

                    if (_onClientCreated != null)
                    {
                        _liveClient.ClientCreated -= _onClientCreated;
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

        var acquired = false;
        try
        {
            await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            acquired = true;
            await client.SendAudioAsync(pcmBytes)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            _needsReset = true;
            Console.WriteLine("Gemini Live send warning: socket disposed while sending.");
        }
        catch (OperationCanceledException)
        {
            _needsReset = true;
        }
        catch (SocketException ex)
        {
            _needsReset = true;
            Console.WriteLine($"Gemini Live send warning: Socket error: {ex.SocketErrorCode}");
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
        finally
        {
            if (acquired)
            {
                _sendLock.Release();
            }
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

        var acquired = false;
        try
        {
            await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            acquired = true;
            await liveClient.SentTextAsync("Hello. You are an assistant, pretend you're being called on the phone.")
                .ConfigureAwait(false);
            _sentGreeting = true;
            Interlocked.Exchange(ref _lastActivityMs, Environment.TickCount64);
        }
        catch (OperationCanceledException)
        {
            _needsReset = true;
        }
        catch (Exception ex)
        {
            if (ex is ObjectDisposedException)
            {
                ResetLiveClient();
            }
            Console.WriteLine($"Gemini Live greeting send error: {ex.Message}");
        }
        finally
        {
            if (acquired)
            {
                _sendLock.Release();
            }
        }
    }

    public void HandleGeminiAudio(ReadOnlySpan<short> pcm16k)
    {
        if (!_resamplerConfigurationValid)
        {
            return;
        }

        var requiredSourceSamples = _geminiOutputFrameSamples;
        if (pcm16k.Length < requiredSourceSamples)
        {
            return;
        }

        try
        {
            AudioResampler.DownsampleBy3(pcm16k[..requiredSourceSamples], _frame8kFromGemini);
        }
        catch (ArgumentException ex)
        {
            _needsReset = true;
            Console.WriteLine($"Gemini Live resample warning: {ex.Message}");
            return;
        }
        var pcmuFrame = new byte[Config.PayloadBytes];
        for (int i = 0; i < Config.PayloadBytes; i++)
        {
            pcmuFrame[i] = G711.Pcm16ToMuLaw(_frame8kFromGemini[i]);
        }

        if (_onPcmuFrame == null)
        {
            return;
        }

        if (!_pcmuFrames.Writer.TryWrite(pcmuFrame))
        {
            Interlocked.Increment(ref _droppedPcmuFrames);
        }
    }

    private void HandleGeminiAudioBytes(byte[] buffer)
    {
        if (buffer.Length < sizeof(short))
        {
            return;
        }

        Interlocked.Exchange(ref _lastActivityMs, Environment.TickCount64);
        var samples = new short[buffer.Length / sizeof(short)];
        Buffer.BlockCopy(buffer, 0, samples, 0, samples.Length * sizeof(short));

        List<short[]>? frames = null;
        lock (_audioLock)
        {
            _geminiAudioBuffer.AddRange(samples);
            while (_geminiAudioBuffer.Count >= _geminiOutputFrameSamples)
            {
                frames ??= new List<short[]>();
                var frame = _geminiAudioBuffer.GetRange(0, _geminiOutputFrameSamples);
                _geminiAudioBuffer.RemoveRange(0, _geminiOutputFrameSamples);
                frames.Add(frame.ToArray());
            }
        }

        if (frames == null)
        {
            return;
        }

        foreach (var frame in frames)
        {
            HandleGeminiAudio(frame);
        }
    }

    private bool ValidateResamplerBuffers()
    {
        var valid = true;
        if (_geminiInputFrameSamples < _frame8k.Length * 2)
        {
            Console.WriteLine(
                $"Gemini Live: input resampler buffer mismatch; expected at least {_frame8k.Length * 2} samples but got {_geminiInputFrameSamples}.");
            valid = false;
        }

        if (_geminiOutputFrameSamples != Config.GeminiOutputSamplesPerFrame)
        {
            Console.WriteLine(
                $"Gemini Live: output resampler mismatch; expected {Config.GeminiOutputSamplesPerFrame} samples but got {_geminiOutputFrameSamples}.");
            valid = false;
        }

        return valid;
    }

    private bool TryUpsampleFrame()
    {
        if (_frame16k.Length < _frame8k.Length * 2 || _pcmBytes.Length < _frame16k.Length * sizeof(short))
        {
            Console.WriteLine("Gemini Live: resampler buffer mismatch; skipping frame.");
            return false;
        }

        try
        {
            AudioResampler.Upsample2x(_frame8k, _frame16k);
            Buffer.BlockCopy(_frame16k, 0, _pcmBytes, 0, _pcmBytes.Length);
            return true;
        }
        catch (ArgumentException ex)
        {
            _needsReset = true;
            var failures = Interlocked.Increment(ref _resampleFailures);
            Console.WriteLine($"Gemini Live resample error ({failures}): {ex.Message}");
            return false;
        }
    }

    private void HandleAudioChunkEvent(object? sender, object? args)
    {
        try
        {
            if (args == null)
            {
                return;
            }

            var bufferProperty = args.GetType().GetProperty("Buffer");
            if (bufferProperty?.GetValue(args) is byte[] buffer)
            {
                if (!_audioChunks.Writer.TryWrite(buffer))
                {
                    Interlocked.Increment(ref _droppedAudioChunks);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL] Gemini Event Error: {ex.GetType().Name}: {ex.Message}");
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
            _connected = false;
            _needsReset = true;
            Console.WriteLine($"Gemini Live connect error: {ex.Message}");
            // Intentionally avoid any recursive connect attempts here.
            // The outer StreamLoop handles reconnection scheduling.
            return;
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
        _audioChunks.Writer.TryComplete();
        _pcmuFrames.Writer.TryComplete();
        try { _task?.Wait(2000); } catch { }
        try { _audioChunkTask?.Wait(2000); } catch { }
        try { _pcmuFrameTask?.Wait(2000); } catch { }
        ResetLiveClient();
        _task = null;
        _audioChunkTask = null;
        _pcmuFrameTask = null;
        _running = false;
    }
}
