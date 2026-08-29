using System.Diagnostics;
using System.IO;

namespace SonyBraviaControl.Services;

public sealed class AdbService : IAdbService
{
    private readonly SemaphoreSlim _fastShellLock = new(1, 1);
    private Process? _fastShell;
    private string? _fastShellSerial;
    private string? _fastShellAdbPath;

    public string ResolveAdbPath(string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
            return preferredPath;

        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var path = Environment.GetEnvironmentVariable("PATH");

        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), executable);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloads))
                {
                    foreach (var directory in Directory.EnumerateDirectories(downloads, "platform-tools*", SearchOption.TopDirectoryOnly))
                    {
                        var directCandidate = Path.Combine(directory, "adb.exe");
                        if (File.Exists(directCandidate)) return directCandidate;

                        var nestedCandidate = Path.Combine(directory, "platform-tools", "adb.exe");
                        if (File.Exists(nestedCandidate)) return nestedCandidate;
                    }
                }
            }
            catch { }
        }

        return executable;
    }

    public async Task<string> ConnectAsync(string adbPath, string serial, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(adbPath, ["connect", serial], cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.ErrorText);

        // Keep one adb shell alive. Remote key presses can then be written to the
        // existing LAN connection instead of launching a new adb process per click.
        try { await EnsureFastShellAsync(adbPath, serial, cancellationToken); }
        catch { /* One-shot ADB remains available as a fallback. */ }

        return result.OutputText.Trim();
    }

    public async Task DisconnectAsync(string adbPath, string serial, CancellationToken cancellationToken = default)
    {
        await StopFastShellAsync();
        _ = await RunAsync(adbPath, ["disconnect", serial], cancellationToken);
    }

    public async Task<bool> IsConnectedAsync(string adbPath, string serial, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(adbPath, ["devices"], cancellationToken);
        return result.OutputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.StartsWith(serial, StringComparison.OrdinalIgnoreCase) && line.EndsWith("device", StringComparison.OrdinalIgnoreCase));
    }

    public async Task SendKeyAsync(string adbPath, string serial, string keyCode, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendFastShellCommandAsync(adbPath, serial, $"input keyevent {keyCode}", cancellationToken);
            return;
        }
        catch
        {
            await StopFastShellAsync();
        }

        EnsureSuccess(await RunAsync(adbPath, ["-s", serial, "shell", "input", "keyevent", keyCode], cancellationToken));
    }

    public async Task SendTextAsync(string adbPath, string serial, string text, CancellationToken cancellationToken = default)
    {
        var adbText = text.Replace(" ", "%s", StringComparison.Ordinal);
        EnsureSuccess(await RunAsync(adbPath, ["-s", serial, "shell", "input", "text", adbText], cancellationToken));
    }

    public async Task LaunchPackageAsync(string adbPath, string serial, string packageName, CancellationToken cancellationToken = default)
        => EnsureSuccess(await RunAsync(adbPath, ["-s", serial, "shell", "monkey", "-p", packageName, "-c", "android.intent.category.LAUNCHER", "1"], cancellationToken));

    public async Task RebootAsync(string adbPath, string serial, CancellationToken cancellationToken = default)
    {
        EnsureSuccess(await RunAsync(adbPath, ["-s", serial, "reboot"], cancellationToken));
        await StopFastShellAsync();
    }

    public async Task<string> GetModelAsync(string adbPath, string serial, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(adbPath, ["-s", serial, "shell", "getprop", "ro.product.model"], cancellationToken);
        return result.ExitCode == 0 ? result.OutputText.Trim() : string.Empty;
    }

    private async Task SendFastShellCommandAsync(string adbPath, string serial, string command, CancellationToken cancellationToken)
    {
        await _fastShellLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureFastShellCoreAsync(adbPath, serial, cancellationToken);

            if (_fastShell is null || _fastShell.HasExited)
                throw new InvalidOperationException("La sesión ADB rápida no está disponible.");

            await _fastShell.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
            await _fastShell.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _fastShellLock.Release();
        }
    }

    private async Task EnsureFastShellAsync(string adbPath, string serial, CancellationToken cancellationToken)
    {
        await _fastShellLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureFastShellCoreAsync(adbPath, serial, cancellationToken);
        }
        finally
        {
            _fastShellLock.Release();
        }
    }

    private async Task EnsureFastShellCoreAsync(string adbPath, string serial, CancellationToken cancellationToken)
    {
        if (_fastShell is { HasExited: false } &&
            string.Equals(_fastShellSerial, serial, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_fastShellAdbPath, adbPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StopFastShellCore();

        var startInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add(serial);
        startInfo.ArgumentList.Add("shell");

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("No se pudo iniciar la sesión ADB rápida.");
        }

        _fastShell = process;
        _fastShellSerial = serial;
        _fastShellAdbPath = adbPath;

        // Give adb a tiny window to fail immediately if the device went offline.
        await Task.Delay(35, cancellationToken);
        if (process.HasExited)
        {
            StopFastShellCore();
            throw new InvalidOperationException("La tele no aceptó la sesión ADB rápida.");
        }
    }

    private async Task StopFastShellAsync()
    {
        await _fastShellLock.WaitAsync();
        try { StopFastShellCore(); }
        finally { _fastShellLock.Release(); }
    }

    private void StopFastShellCore()
    {
        if (_fastShell is null) return;

        try
        {
            if (!_fastShell.HasExited)
            {
                try
                {
                    _fastShell.StandardInput.WriteLine("exit");
                    _fastShell.StandardInput.Flush();
                }
                catch { }

                if (!_fastShell.WaitForExit(150))
                    _fastShell.Kill(entireProcessTree: true);
            }
        }
        catch { }
        finally
        {
            _fastShell.Dispose();
            _fastShell = null;
            _fastShellSerial = null;
            _fastShellAdbPath = null;
        }
    }

    private static void EnsureSuccess(ProcessResult result)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.ErrorText) ? result.OutputText : result.ErrorText);
    }

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyCollection<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try { process.Start(); }
        catch (Exception ex) { throw new InvalidOperationException($"No se pudo ejecutar ADB en '{executable}'.", ex); }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private sealed record ProcessResult(int ExitCode, string OutputText, string ErrorText);
}
