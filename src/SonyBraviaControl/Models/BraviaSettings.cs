namespace SonyBraviaControl.Models;

public sealed class BraviaSettings
{
    public string IpAddress { get; set; } = "192.168.1.2";
    public int Port { get; set; } = 5555;
    public string MacAddress { get; set; } = string.Empty;
    public string AdbPath { get; set; } = string.Empty;
    public bool AutoConnect { get; set; } = true;
}
