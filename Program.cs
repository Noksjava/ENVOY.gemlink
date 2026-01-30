using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SipAiGateway;

internal static class EntryPoint
{
    [STAThread]
    private static void Main()
    {
        ConfigureAssemblyResolution();
        using var timer = HighResolutionTimer.TryEnable1ms();

        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch { }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(AppSettings.Load()));
    }

    private static void ConfigureAssemblyResolution()
    {
        var libDirectory = Path.Combine(AppContext.BaseDirectory, "lib");
        if (!Directory.Exists(libDirectory))
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var candidatePath = Path.Combine(libDirectory, $"{name.Name}.dll");
            return File.Exists(candidatePath)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidatePath)
                : null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            var candidatePath = Path.Combine(libDirectory, $"{name}.dll");
            if (!File.Exists(candidatePath))
            {
                return IntPtr.Zero;
            }

            return NativeLibrary.TryLoad(candidatePath, out var handle)
                ? handle
                : IntPtr.Zero;
        };
    }
}
