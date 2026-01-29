 using System;

namespace SipAiGateway;

public static class RtpPacket
{
    public const int HeaderSize = 12;

    public static void WriteHeader(Span<byte> pkt, byte payloadType, ushort seq, uint timestamp, uint ssrc)
    {
        // V=2, no padding/ext, CC=0
        pkt[0] = 0x80;
        pkt[1] = payloadType; // M=0, PT=payloadType
        pkt[2] = (byte)(seq >> 8);
        pkt[3] = (byte)(seq & 0xFF);

        pkt[4] = (byte)(timestamp >> 24);
        pkt[5] = (byte)(timestamp >> 16);
        pkt[6] = (byte)(timestamp >> 8);
        pkt[7] = (byte)(timestamp & 0xFF);

        pkt[8] = (byte)(ssrc >> 24);
        pkt[9] = (byte)(ssrc >> 16);
        pkt[10] = (byte)(ssrc >> 8);
        pkt[11] = (byte)(ssrc & 0xFF);
    }

    public static bool TryParse(ReadOnlySpan<byte> pkt, out byte payloadType, out ReadOnlySpan<byte> payload)
    {
        payloadType = 0;
        payload = default;

        if (pkt.Length < HeaderSize) return false;
        if ((pkt[0] & 0xC0) != 0x80) return false; // version 2
        int cc = pkt[0] & 0x0F;
        int headerLen = HeaderSize + cc * 4;
        if (pkt.Length < headerLen) return false;

        payloadType = (byte)(pkt[1] & 0x7F);
        payload = pkt[headerLen..];
        return true;
    }
}
