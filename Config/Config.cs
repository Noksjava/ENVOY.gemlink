namespace SipAiGateway;

public static class Config
{
    // Your PC LAN IP (the IP you want advertised in SDP).
    public const string LocalIp = "192.168.88.254";

    // SIP and RTP ports.
    public const int SipPort = 5070;
    public const int RtpPort = 40000;

    // Dial string accepted by the SIP phone.
    public const string ContactUser = "ai";

    // RTP settings (PCMU 8kHz, 20ms).
    public const int SampleRate = 8000;
    public const int FrameMs = 20;
    public const int SamplesPerFrame = SampleRate * FrameMs / 1000; // 160
    public const int PayloadBytes = SamplesPerFrame; // PCMU: 1 byte per sample
    public const byte PayloadTypePcmu = 0;

    // Debug/testing (used when Settings Test mode is enabled)
    public const int EchoDelayFrames = 50;    // 50 * 20ms = 1s, very obvious

    // PCM buffering (for AI integration).
    public const int PcmRxBufferMs = 2000; // 2 seconds
    public const int PcmRxBufferSamples = SampleRate * PcmRxBufferMs / 1000;

    // Gemini Live configuration moved to Settings UI.
    public const int GeminiInputSampleRate = 16000;
    public const int GeminiOutputSampleRate = 24000;
    public const int GeminiInputSamplesPerFrame = GeminiInputSampleRate * FrameMs / 1000;   // 320
    public const int GeminiOutputSamplesPerFrame = GeminiOutputSampleRate * FrameMs / 1000; // 480
}
