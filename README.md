# ENVOY.Gemlink-SIP

A lightweight SIP -> AI (Gemini) voice gateway focused on direct IP calling and real time audio bridging. The app acts as a minimal SIP endpoint: it answers INVITEs, negotiates RTP via SDP, and streams PCMU audio over RTP. The current build is a Windows GUI with a central log console (subject to change later).

## What it does

- Listens on SIP UDP (default 5070) and RTP UDP (default 40000).
- Accepts direct IP calls from any SIP phone (w/o PBX).
- Sends and receives RTP (PCMU, 8 kHz, 20 ms frames).
- A TEST mode toggle in Settings (default ON, echo with 2s delay to test whether the device itself works).
- Buffers decoded PCM audio for AI ingestion.
- Streams PCM audio to Gemini Live and injects Gemini audio back into RTP (only Gemini works for now because Gemini Live voice API is free to use w/o paying).
- Selectable Gemini voices with descriptive labels in Settings.
- First-launch setup wizard to configure API keys, models, and test mode.

## Architecture (high level)

- **SipGateway**: SIP signaling (INVITE/ACK/BYE) and SDP negotiation.
- **RtpSession**: real-time RTP TX/RX with stable 20 ms pacing.
- **G711**: PCMU encoding/decoding.
- **AudioRingBuffer**: PCM staging for AI integration.
- **GeminiLiveClient**: Gemini Live bridge for sending PCM audio and receiving Gemini responses.
- **MainForm**: Windows GUI with log console.

## How to run

1. Get API key from AIStudio (google).
2. Run the Windows app. 
3. Place a direct IP SIP call to the host IP and port (5070 by default) shown in the UI log.
4. Confirm RTP audio flow in the log console / listen for echo.
5. Paste the API Key during startup / in settings.
6. Run it Live.

## Future goals

- Add barge-in detection (VAD/energy-based) for real-time interruption.
- Improve latency tuning and add optional jitter buffering.
- Support multiple concurrent calls and optional codecs beyond PCMU.
- Support more API proviers.
- Improve latency / quality / etc.
- Fix buffering bugs.

## Version

Current version: **1.0.0.1A**
