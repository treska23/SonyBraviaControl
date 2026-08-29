namespace SonyBraviaControl.Services;

public interface IAdbService
{
    string ResolveAdbPath(string? preferredPath = null);
    Task<string> ConnectAsync(string adbPath, string serial, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string adbPath, string serial, CancellationToken cancellationToken = default);
    Task<bool> IsConnectedAsync(string adbPath, string serial, CancellationToken cancellationToken = default);
    Task SendKeyAsync(string adbPath, string serial, string keyCode, CancellationToken cancellationToken = default);
    Task SendTextAsync(string adbPath, string serial, string text, CancellationToken cancellationToken = default);
    Task LaunchPackageAsync(string adbPath, string serial, string packageName, CancellationToken cancellationToken = default);
    Task RebootAsync(string adbPath, string serial, CancellationToken cancellationToken = default);
    Task<string> GetModelAsync(string adbPath, string serial, CancellationToken cancellationToken = default);
}
