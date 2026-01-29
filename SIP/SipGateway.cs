using Org.BouncyCastle.Crypto;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using System;
using System.Collections.Generic;
using System.Net;

namespace SipAiGateway;

public sealed class SipGateway : IDisposable
{
    private readonly SIPTransport _transport = new();
    private RtpSession? _rtpSession;
    private readonly AppSettings _settings;

    public SipGateway(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Start()
    {
        _transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, Config.SipPort)));
        _transport.SIPTransportRequestReceived += OnSipRequest;

        Console.WriteLine("SIP gateway ready.");
    }

    private async System.Threading.Tasks.Task OnSipRequest(SIPEndPoint localEp, SIPEndPoint remoteEp, SIPRequest req)
    {
        try
        {
            Console.WriteLine($"<-- {req.Method} {req.URI} from {remoteEp} (Call-ID: {req.Header?.CallId})");

            if (req.Method == SIPMethodsEnum.INVITE)
            {
                var offer = SdpHelper.ParseOffer(req.Body);
                if (offer == null)
                {
                    var bad = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.BadRequest, null);
                    await _transport.SendResponseAsync(bad);
                    return;
                }

                // Store remote RTP endpoint from SDP offer.
                var remoteRtp = new IPEndPoint(IPAddress.Parse(offer.ConnectionIp), offer.AudioPort);

                Console.WriteLine($"Remote RTP (from SDP): {remoteRtp} | offered payloads: {string.Join(",", offer.Payloads)}");

                // 100 Trying
                await _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Trying, null));

                // 200 OK with our SDP answer (PCMU).
                var ok = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null);
                ok.Header.To.ToTag = Ids.NewTag();

                ok.Header.Contact = new List<SIPContactHeader>
                {
                    new(null, SIPURI.ParseSIPURI($"sip:{Config.ContactUser}@{Config.LocalIp}:{Config.SipPort}"))
                };

                ok.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
                ok.Body = SdpHelper.BuildAnswer(Config.LocalIp, Config.RtpPort);
                ok.Header.ContentLength = ok.Body.Length;

                await _transport.SendResponseAsync(ok);
                Console.WriteLine("--> 200 OK sent with SDP (PCMU). Waiting for ACK.");

                // Prepare RTP session but start it on ACK.
                _rtpSession?.Dispose();
                _rtpSession = new RtpSession(remoteRtp, Config.LocalIp, Config.RtpPort, _settings);
            }
            else if (req.Method == SIPMethodsEnum.ACK)
            {
                Console.WriteLine("<-- ACK (call established). Starting RTP.");
                _rtpSession?.Start();
            }
            else if (req.Method == SIPMethodsEnum.BYE)
            {
                Console.WriteLine("<-- BYE (ending call).");
                _rtpSession?.Dispose();
                _rtpSession = null;

                await _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null));
            }
            else
            {
                await _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.MethodNotAllowed, null));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SIP handler error: {ex.Message}");
            try
            {
                await _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.InternalServerError, null));
            }
            catch { }
        }
    }

    public void Dispose()
    {
        _rtpSession?.Dispose();
        _transport.Shutdown();
    }
}
