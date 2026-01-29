using System;
using System.Runtime.InteropServices;

namespace SipAiGateway;

public sealed class HighResolutionTimer : IDisposable
{
    private readonly bool _enabled;

    private HighResolutionTimer(bool enabled) => _enabled = enabled;

    public static HighResolutionTimer TryEnable1ms()
    {
        try
        {
            int r = timeBeginPeriod(1);
            return new HighResolutionTimer(r == 0);
        }
        catch
        {
            return new HighResolutionTimer(false);
        }
    }

    public void Dispose()
    {
        if (_enabled)
        {
            try { timeEndPeriod(1); } catch { }
        }
    }

    [DllImport("winmm.dll")]
    private static extern int timeBeginPeriod(int uPeriod);

    [DllImport("winmm.dll")]
    private static extern int timeEndPeriod(int uPeriod);
}
