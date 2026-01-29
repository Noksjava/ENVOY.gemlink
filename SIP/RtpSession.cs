using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;

namespace SipAiGateway;

public sealed class RtpSession : IDisposable
{
    private readonly string _localIp;
    private readonly int _localRtpPort;

    private readonly UdpClient _udp;
    private IPEndPoint _remoteRtp; // will be updated by symmetric RTP

    private readonly CancellationTokenSource _cts = new();
    private Thread? _txThread;
    private Thread? _rxThread;

    private readonly Channel<byte[]> _txFrames = Channel.CreateUnbounded<byte[]>();
    private readonly Queue<byte[]> _echoQueue = new();
    private readonly AudioRingBuffer _rxPcmBuffer;
    private readonly GeminiLiveClient? _geminiLiveClient;
    private readonly bool _aiPatchEnabled;
    private readonly AppSettings _settings;

    // Reusable send packet and silence payload.
    private readonly byte[] _pkt = new byte[RtpPacket.HeaderSize + Config.PayloadBytes];
    private readonly byte[] _silence = new byte[Config.PayloadBytes];

    private ushort _seq;
    private uint _ts;
    private readonly uint _ssrc;

    public RtpSession(IPEndPoint remoteRtpFromSdp, string localIp, int localRtpPort, AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _remoteRtp = remoteRtpFromSdp;
        _localIp = localIp;
        _localRtpPort = localRtpPort;
        _rxPcmBuffer = new AudioRingBuffer(Config.PcmRxBufferSamples);
        bool hasApiKey = !string.IsNullOrWhiteSpace(_settings.GeminiApiKey);
        _aiPatchEnabled = !_settings.TestMode && hasApiKey;
        _geminiLiveClient = _aiPatchEnabled
            ? new GeminiLiveClient(
                ReadPcm,
                _settings.GeminiEndpoint,
                _settings.GeminiApiKey,
                _settings.GeminiModel,
                _settings.GeminiSystemPrompt,
                _settings.GeminiVoice,
                EnqueueGeminiPcmuFrame)
            : null;

        for (int i = 0; i < _silence.Length; i++) _silence[i] = 0xFF; // PCMU silence

        _ssrc = Ids.NewSsrc();
        _udp = new UdpClient(new IPEndPoint(IPAddress.Parse(_localIp), _localRtpPort));
        Console.WriteLine($"RTP bound: {_udp.Client.LocalEndPoint}");
    }

    public void Start()
    {
        if (_txThread != null) return;

        // Kick a beep immediately.
        EnqueueBeep();

        _rxThread = new Thread(RxLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _txThread = new Thread(TxLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };

        _rxThread.Start();
        _txThread.Start();

        if (_aiPatchEnabled)
        {
            Console.WriteLine("Live mode: patching audio to Gemini.");
            _geminiLiveClient?.Start();
        }
        else if (_settings.TestMode)
        {
            Console.WriteLine("Test mode: echo enabled.");
        }
        else
        {
            Console.WriteLine("Live mode: Gemini patch disabled (no API key configured).");
        }
    }

    private void TxLoop()
    {
        var ct = _cts.Token;
        var sw = Stopwatch.StartNew();
        long nextMs = 0;
        long lastLog = 0;
        int sent = 0;

        // Preload silence payload.
        Buffer.BlockCopy(_silence, 0, _pkt, RtpPacket.HeaderSize, Config.PayloadBytes);

        while (!ct.IsCancellationRequested)
        {
            // Pull a frame if available; otherwise keep silence already present.
            if (_txFrames.Reader.TryRead(out var frame) && frame.Length == Config.PayloadBytes)
            {
                Buffer.BlockCopy(frame, 0, _pkt, RtpPacket.HeaderSize, Config.PayloadBytes);
            }

            RtpPacket.WriteHeader(_pkt, Config.PayloadTypePcmu, _seq, _ts, _ssrc);

            // Send
            _udp.Send(_pkt, _pkt.Length, _remoteRtp);

            _seq++;
            _ts += (uint)Config.SamplesPerFrame;
            sent++;

            // Pace 20ms
            nextMs += Config.FrameMs;
            long now = sw.ElapsedMilliseconds;
            long wait = nextMs - now;

            if (wait > 2) Thread.Sleep((int)(wait - 1));
            while (sw.ElapsedMilliseconds < nextMs) Thread.SpinWait(60);

            // Log once per second
            now = sw.ElapsedMilliseconds;
            if (now - lastLog >= 1000)
            {
                lastLog = now;
                Console.WriteLine($"RTP TX ok. sent={sent} remote={_remoteRtp}");
            }
        }
    }

    private void RxLoop()
    {
        var ct = _cts.Token;
        long lastLog = Environment.TickCount64;
        int rx = 0;

        Console.WriteLine("RTP RX loop running...");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var buf = _udp.Receive(ref remote);

                // Symmetric RTP: learn the real remote endpoint.
                _remoteRtp = remote;

                rx++;

                if (RtpPacket.TryParse(buf, out var pt, out var payload) &&
                    pt == Config.PayloadTypePcmu &&
                    payload.Length >= Config.PayloadBytes)
                {
                    var payloadFrame = payload[..Config.PayloadBytes];
                    Span<short> pcmFrame = stackalloc short[Config.SamplesPerFrame];
                    G711.MuLawToPcm16(payloadFrame, pcmFrame);
                    _rxPcmBuffer.Write(pcmFrame);

                    if (_settings.TestMode)
                    {
                        // Copy out exactly 160 bytes for echo queue.
                        var frame = new byte[Config.PayloadBytes];
                        payloadFrame.CopyTo(frame);

                        _echoQueue.Enqueue(frame);
                        if (_echoQueue.Count > Config.EchoDelayFrames)
                        {
                            var outFrame = _echoQueue.Dequeue();
                            _txFrames.Writer.TryWrite(outFrame);
                        }
                    }
                }

                var now = Environment.TickCount64;
                if (now - lastLog >= 1000)
                {
                    lastLog = now;
                    Console.WriteLine($"RTP RX ok. rx={rx} from={remote}");
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"RTP RX socket error: {ex.SocketErrorCode}");
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RTP RX error: {ex.Message}");
                break;
            }
        }
    }

    private void EnqueueBeep()
    {
        // 0.5s, lower 440Hz tone generated as PCMU frames.
        const double frequencyHz = 440.0;
        const double amplitude = 4500.0;
        int totalSamples = Config.SampleRate / 2; // 0.5 sec
        int idx = 0;

        while (idx + Config.SamplesPerFrame <= totalSamples)
        {
            var frame = new byte[Config.PayloadBytes];
            for (int i = 0; i < Config.SamplesPerFrame; i++)
            {
                double t = (idx + i) / (double)Config.SampleRate;
                short pcm = (short)(Math.Sin(2 * Math.PI * frequencyHz * t) * amplitude);
                frame[i] = G711.Pcm16ToMuLaw(pcm);
            }
            idx += Config.SamplesPerFrame;
            _txFrames.Writer.TryWrite(frame);
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _udp.Close(); } catch { }
        try { _geminiLiveClient?.Dispose(); } catch { }

        _txThread = null;
        _rxThread = null;
    }

    private void EnqueueGeminiPcmuFrame(byte[] frame)
    {
        if (frame.Length != Config.PayloadBytes)
        {
            return;
        }

        _txFrames.Writer.TryWrite(frame);
    }

    public int ReadPcm(Span<short> destination)
    {
        return _rxPcmBuffer.Read(destination);
    }

    public int BufferedPcmSamples => _rxPcmBuffer.Count;
}
