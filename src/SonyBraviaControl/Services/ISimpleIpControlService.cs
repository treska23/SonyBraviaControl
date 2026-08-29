namespace SonyBraviaControl.Services;

public interface ISimpleIpControlService
{
    bool SupportsKey(string androidKeyCode);
    Task<bool> ConnectAsync(string ipAddress, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<bool> SendKeyAsync(string ipAddress, string androidKeyCode, CancellationToken cancellationToken = default);
}
