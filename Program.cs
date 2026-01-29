using System;
using System.Diagnostics;
using System.Windows.Forms;
using SipAiGateway;

internal static class EntryPoint
{
    [STAThread]
    private static void Main()
    {
        using var timer = HighResolutionTimer.TryEnable1ms();

        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch { }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(AppSettings.Load()));
    }
}
