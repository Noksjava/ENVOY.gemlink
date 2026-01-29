using System;

namespace SipAiGateway;

public static class G711
{
    private static readonly short[] MuLawDecodeTable = BuildMuLawDecodeTable();

    public static byte Pcm16ToMuLaw(short sample)
    {
        const int BIAS = 0x84;
        const int CLIP = 32635;

        int sign = (sample >> 8) & 0x80;
        int pcm = sign != 0 ? -sample : sample;
        if (pcm > CLIP) pcm = CLIP;

        pcm += BIAS;

        int exponent = 7;
        for (int expMask = 0x4000; (pcm & expMask) == 0 && exponent > 0; exponent--, expMask >>= 1) { }

        int mantissa = (pcm >> (exponent + 3)) & 0x0F;
        int ulaw = ~(sign | (exponent << 4) | mantissa);
        return (byte)ulaw;
    }

    public static short MuLawToPcm16(byte ulaw)
    {
        return MuLawDecodeTable[ulaw];
    }

    public static void MuLawToPcm16(ReadOnlySpan<byte> muLaw, Span<short> pcm16)
    {
        if (pcm16.Length < muLaw.Length)
        {
            throw new ArgumentException("Destination span is too small.", nameof(pcm16));
        }

        for (int i = 0; i < muLaw.Length; i++)
        {
            pcm16[i] = MuLawDecodeTable[muLaw[i]];
        }
    }

    private static short[] BuildMuLawDecodeTable()
    {
        const int BIAS = 0x84;
        var table = new short[256];

        for (int i = 0; i < table.Length; i++)
        {
            int u = ~i & 0xFF;
            int sign = u & 0x80;
            int exponent = (u >> 4) & 0x07;
            int mantissa = u & 0x0F;

            int sample = ((mantissa << 3) + BIAS) << exponent;
            sample -= BIAS;

            table[i] = (short)(sign != 0 ? -sample : sample);
        }

        return table;
    }
}
