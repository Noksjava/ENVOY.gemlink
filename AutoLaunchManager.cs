using System;
using System.IO;
using Microsoft.Win32;

namespace SipAiGateway;

public static class AutoLaunchManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ENVOY.Gemlink-SIP";

    public static void Apply(bool enable)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                return;
            }

            if (enable)
            {
                runKey.SetValue(AppName, $"\"{GetExecutablePath()}\"");
            }
            else
            {
                runKey.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore auto-launch registration failures.
        }
    }

    private static string GetExecutablePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            return exePath;
        }

        return Path.Combine(baseDir, "ENVOY.Gemlink-SIP.exe");
    }
}
