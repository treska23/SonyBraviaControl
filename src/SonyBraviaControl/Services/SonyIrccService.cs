using System.Net.Http.Headers;
using System.Text;

namespace SonyBraviaControl.Services;

public sealed class SonyIrccService : IIrccService, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> IrccCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KEYCODE_POWER"] = "AAAAAQAAAAEAAAAVAw==",
            ["KEYCODE_TV_INPUT"] = "AAAAAQAAAAEAAAAlAw==",
            ["KEYCODE_TV_INPUT_HDMI_1"] = "AAAAAgAAABoAAABaAw==",
            ["KEYCODE_TV_INPUT_HDMI_2"] = "AAAAAgAAABoAAABbAw==",
            ["KEYCODE_TV_INPUT_HDMI_3"] = "AAAAAgAAABoAAABcAw==",
            ["KEYCODE_TV_INPUT_HDMI_4"] = "AAAAAgAAABoAAABdAw==",
            ["KEYCODE_DPAD_UP"] = "AAAAAQAAAAEAAAB0Aw==",
            ["KEYCODE_DPAD_DOWN"] = "AAAAAQAAAAEAAAB1Aw==",
            ["KEYCODE_DPAD_RIGHT"] = "AAAAAQAAAAEAAAAzAw==",
            ["KEYCODE_DPAD_LEFT"] = "AAAAAQAAAAEAAAA0Aw==",
            ["KEYCODE_DPAD_CENTER"] = "AAAAAQAAAAEAAABlAw==",
            ["KEYCODE_HOME"] = "AAAAAQAAAAEAAABgAw==",
            ["KEYCODE_BACK"] = "AAAAAgAAAJcAAAAjAw==",
            ["KEYCODE_MENU"] = "AAAAAgAAAJcAAAA2Aw==",
            ["KEYCODE_VOLUME_UP"] = "AAAAAQAAAAEAAAASAw==",
            ["KEYCODE_VOLUME_DOWN"] = "AAAAAQAAAAEAAAATAw==",
            ["KEYCODE_VOLUME_MUTE"] = "AAAAAQAAAAEAAAAUAw==",
            ["KEYCODE_CHANNEL_UP"] = "AAAAAQAAAAEAAAAQAw==",
            ["KEYCODE_CHANNEL_DOWN"] = "AAAAAQAAAAEAAAARAw==",
            ["KEYCODE_MEDIA_PLAY"] = "AAAAAgAAAJcAAAAaAw==",
            ["KEYCODE_MEDIA_PAUSE"] = "AAAAAgAAAJcAAAAZAw==",
            ["KEYCODE_MEDIA_STOP"] = "AAAAAgAAAJcAAAAYAw==",
            ["KEYCODE_MEDIA_REWIND"] = "AAAAAgAAAJcAAAAcAw==",
            ["KEYCODE_MEDIA_FAST_FORWARD"] = "AAAAAgAAAJcAAAAbAw=="
        };

    private readonly HttpClient _httpClient;

    public SonyIrccService()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 4,
            ConnectTimeout = TimeSpan.FromMilliseconds(700)
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(1200)
        };
    }

    public async Task<bool> ProbeAsync(string ipAddress, string? preSharedKey, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://{ipAddress.Trim()}/sony/system");
            ApplyAuthentication(request, preSharedKey);
            request.Content = new StringContent(
                "{\"method\":\"getRemoteControllerInfo\",\"params\":[],\"id\":1,\"version\":\"1.0\"}",
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SendKeyAsync(string ipAddress, string? preSharedKey, string androidKeyCode, CancellationToken cancellationToken = default)
    {
        if (!IrccCodes.TryGetValue(androidKeyCode, out var irccCode))
            return false;

        var body = $"<?xml version=\"1.0\"?>" +
                   "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                   "<s:Body><u:X_SendIRCC xmlns:u=\"urn:schemas-sony-com:service:IRCC:1\">" +
                   $"<IRCCCode>{irccCode}</IRCCCode>" +
                   "</u:X_SendIRCC></s:Body></s:Envelope>";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://{ipAddress.Trim()}/sony/ircc");
            ApplyAuthentication(request, preSharedKey);
            request.Headers.TryAddWithoutValidation("SOAPACTION", "\"urn:schemas-sony-com:service:IRCC:1#X_SendIRCC\"");
            request.Headers.Connection.Add("keep-alive");

            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=UTF-8");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyAuthentication(HttpRequestMessage request, string? preSharedKey)
    {
        if (!string.IsNullOrWhiteSpace(preSharedKey))
            request.Headers.TryAddWithoutValidation("X-Auth-PSK", preSharedKey.Trim());
    }

    public void Dispose() => _httpClient.Dispose();
}
