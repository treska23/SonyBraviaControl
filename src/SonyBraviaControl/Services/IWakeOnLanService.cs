namespace SonyBraviaControl.Services;

public interface IWakeOnLanService
{
    Task WakeAsync(string macAddress, CancellationToken cancellationToken = default);
}
