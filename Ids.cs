using System;

namespace SipAiGateway;

public static class Ids
{
    public static string NewTag() => Guid.NewGuid().ToString("N")[..10];

    public static uint NewSsrc()
    {
        // Non-zero, random.
        var v = (uint)Random.Shared.NextInt64(1, int.MaxValue);
        return v == 0 ? 1u : v;
    }
}
