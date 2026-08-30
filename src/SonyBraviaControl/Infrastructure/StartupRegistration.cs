using System.IO;
using Microsoft.Win32;

namespace SonyBraviaControl.Infrastructure;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SonyBraviaControl";

    public static void EnsureRegistered()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                return;

            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
                return;

            var command = $"\"{executablePath}\" --startup";
            var current = runKey.GetValue(ValueName) as string;
            if (!string.Equals(current, command, StringComparison.Ordinal))
                runKey.SetValue(ValueName, command, RegistryValueKind.String);
        }
        catch
        {
            // Autostart is a convenience feature. A registry policy or locked-down PC
            // must never prevent the remote itself from launching.
        }
    }
}
