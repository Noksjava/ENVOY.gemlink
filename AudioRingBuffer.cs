using System;

namespace SipAiGateway;

public sealed class AudioRingBuffer
{
    private readonly short[] _buffer;
    private readonly object _lock = new();
    private int _readIndex;
    private int _writeIndex;
    private int _count;

    public AudioRingBuffer(int capacitySamples)
    {
        if (capacitySamples <= 0) throw new ArgumentOutOfRangeException(nameof(capacitySamples));
        _buffer = new short[capacitySamples];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public int Write(ReadOnlySpan<short> samples)
    {
        lock (_lock)
        {
            int written = 0;
            for (int i = 0; i < samples.Length && _count < _buffer.Length; i++)
            {
                _buffer[_writeIndex] = samples[i];
                _writeIndex = (_writeIndex + 1) % _buffer.Length;
                _count++;
                written++;
            }

            return written;
        }
    }

    public int Read(Span<short> destination)
    {
        lock (_lock)
        {
            int read = 0;
            for (int i = 0; i < destination.Length && _count > 0; i++)
            {
                destination[i] = _buffer[_readIndex];
                _readIndex = (_readIndex + 1) % _buffer.Length;
                _count--;
                read++;
            }

            return read;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _readIndex = 0;
            _writeIndex = 0;
            _count = 0;
            Array.Clear(_buffer);
        }
    }
}
