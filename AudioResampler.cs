using System;

namespace SipAiGateway;

public static class AudioResampler
{
    public static void Upsample2x(ReadOnlySpan<short> source, Span<short> destination)
    {
        if (destination.Length < source.Length * 2)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        if (source.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < source.Length - 1; i++)
        {
            short s0 = source[i];
            short s1 = source[i + 1];
            int dstIndex = i * 2;
            destination[dstIndex] = s0;
            destination[dstIndex + 1] = (short)((s0 + s1) / 2);
        }

        int last = (source.Length - 1) * 2;
        destination[last] = source[^1];
        destination[last + 1] = source[^1];
    }

    public static void DownsampleBy3(ReadOnlySpan<short> source, Span<short> destination)
    {
        if (source.Length < destination.Length * 3)
        {
            throw new ArgumentException("Source span is too small.", nameof(source));
        }

        for (int i = 0; i < destination.Length; i++)
        {
            int baseIndex = i * 3;
            int sum = source[baseIndex] + source[baseIndex + 1] + source[baseIndex + 2];
            destination[i] = (short)(sum / 3);
        }
    }

    public static void Downsample2x(ReadOnlySpan<short> source, Span<short> destination)
    {
        if (source.Length < destination.Length * 2)
        {
            throw new ArgumentException("Source span is too small.", nameof(source));
        }

        for (int i = 0; i < destination.Length; i++)
        {
            int baseIndex = i * 2;
            int sum = source[baseIndex] + source[baseIndex + 1];
            destination[i] = (short)(sum / 2);
        }
    }
}
