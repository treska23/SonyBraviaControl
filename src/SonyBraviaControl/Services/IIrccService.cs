namespace SonyBraviaControl.Services;

public interface IIrccService
{
    Task<bool> ProbeAsync(string ipAddress, string? preSharedKey, CancellationToken cancellationToken = default);
    Task<bool> SendKeyAsync(string ipAddress, string? preSharedKey, string androidKeyCode, CancellationToken cancellationToken = default);
}
