using System;
using System.Collections.Generic;

namespace SipAiGateway;

public static class SdpHelper
{
    public sealed record Offer(string ConnectionIp, int AudioPort, IReadOnlyList<string> Payloads);

    public static Offer? ParseOffer(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp)) return null;

        string? ip = null;
        int? port = null;
        var payloads = new List<string>();

        var lines = sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.StartsWith("c=IN IP4 ", StringComparison.OrdinalIgnoreCase))
            {
                ip = line["c=IN IP4 ".Length..].Trim();
            }
            else if (line.StartsWith("m=audio ", StringComparison.OrdinalIgnoreCase))
            {
                // m=audio 11784 RTP/AVP 0 8 18 9 101
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && int.TryParse(parts[1], out var p))
                {
                    port = p;
                    for (int i = 3; i < parts.Length; i++) payloads.Add(parts[i]);
                }
            }
        }

        if (ip == null || port == null) return null;
        return new Offer(ip, port.Value, payloads);
    }

    public static string BuildAnswer(string localIp, int rtpPort)
    {
        // PCMU(0) + telephone-event(101)
        return
$@"v=0
o=- 0 0 IN IP4 {localIp}
s=ENVOY.Gemlink-SIP
c=IN IP4 {localIp}
t=0 0
m=audio {rtpPort} RTP/AVP 0 101
a=rtpmap:0 PCMU/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-15
a=sendrecv
";
    }
}
