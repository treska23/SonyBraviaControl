using System.Net;
using System.Net.Sockets;

namespace SonyBraviaControl.Services;

public sealed class WakeOnLanService : IWakeOnLanService
{
    public async Task WakeAsync(string macAddress, CancellationToken cancellationToken = default)
    {
        var macBytes = ParseMac(macAddress);
        var packet = new byte[6 + (16 * macBytes.Length)];

        for (var i = 0; i < 6; i++) packet[i] = 0xFF;
        for (var i = 6; i < packet.Length; i += macBytes.Length)
            Buffer.BlockCopy(macBytes, 0, packet, i, macBytes.Length);

        using var udp = new UdpClient { EnableBroadcast = true };
        await udp.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, 9), cancellationToken);
    }

    private static byte[] ParseMac(string macAddress)
    {
        var normalized = macAddress.Replace(":", string.Empty, StringComparison.Ordinal)
                                   .Replace("-", string.Empty, StringComparison.Ordinal)
                                   .Trim();

        if (normalized.Length != 12)
            throw new FormatException("La MAC debe tener 12 dígitos hexadecimales.");

        return Enumerable.Range(0, 6)
            .Select(index => Convert.ToByte(normalized.Substring(index * 2, 2), 16))
            .ToArray();
    }
}
