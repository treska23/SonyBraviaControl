using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace SonyBraviaControl.Services;

public sealed class SonySimpleIpControlService : ISimpleIpControlService, IDisposable
{
    private const int Port = 20060;

    private static readonly IReadOnlyDictionary<string, int> IrCodes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["KEYCODE_HOME"] = 6,
            ["KEYCODE_MENU"] = 7,
            ["KEYCODE_BACK"] = 8,
            ["KEYCODE_DPAD_UP"] = 9,
            ["KEYCODE_DPAD_DOWN"] = 10,
            ["KEYCODE_DPAD_RIGHT"] = 11,
            ["KEYCODE_DPAD_LEFT"] = 12,
            ["KEYCODE_DPAD_CENTER"] = 13,
            ["KEYCODE_VOLUME_UP"] = 30,
            ["KEYCODE_VOLUME_DOWN"] = 31,
            ["KEYCODE_VOLUME_MUTE"] = 32,
            ["KEYCODE_CHANNEL_UP"] = 33,
            ["KEYCODE_CHANNEL_DOWN"] = 34,
            ["KEYCODE_MEDIA_FAST_FORWARD"] = 77,
            ["KEYCODE_MEDIA_PLAY"] = 78,
            ["KEYCODE_MEDIA_PLAY_PAUSE"] = 78,
            ["KEYCODE_MEDIA_REWIND"] = 79,
            ["KEYCODE_MEDIA_STOP"] = 81,
            ["KEYCODE_MEDIA_PAUSE"] = 84,
            ["KEYCODE_POWER"] = 98,
            ["KEYCODE_TV_INPUT"] = 101,
            ["KEYCODE_SLEEP"] = 104,
            ["KEYCODE_TV_INPUT_HDMI_1"] = 124,
            ["KEYCODE_TV_INPUT_HDMI_2"] = 125,
            ["KEYCODE_TV_INPUT_HDMI_3"] = 126,
            ["KEYCODE_TV_INPUT_HDMI_4"] = 127
        };

    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private string? _connectedIp;

    public bool SupportsKey(string androidKeyCode) => IrCodes.ContainsKey(androidKeyCode);

    public async Task<bool> ConnectAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectedAsync(ipAddress, cancellationToken);
            return true;
        }
        catch
        {
            await DisconnectAsync();
            return false;
        }
    }

    public async Task<bool> SendKeyAsync(string ipAddress, string androidKeyCode, CancellationToken cancellationToken = default)
    {
        if (!IrCodes.TryGetValue(androidKeyCode, out var irCode))
            return false;

        try
        {
            await EnsureConnectedAsync(ipAddress, cancellationToken);
            var frame = BuildIrccFrame(irCode);

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                if (_stream is null)
                    return false;

                await _stream.WriteAsync(frame, cancellationToken);
                // NetworkStream.Flush is a no-op. Do not wait for Sony's acknowledgement:
                // the command is already on the TCP socket and perceived latency matters here.
            }
            finally
            {
                _writeLock.Release();
            }

            return true;
        }
        catch
        {
            await DisconnectAsync();
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            _readerCts?.Cancel();
            _stream?.Dispose();
            _client?.Dispose();
            _stream = null;
            _client = null;
            _connectedIp = null;
            _readerCts?.Dispose();
            _readerCts = null;
            _readerTask = null;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task EnsureConnectedAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var ip = ipAddress.Trim();
        if (_client is { Connected: true } && _stream is not null &&
            string.Equals(_connectedIp, ip, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is { Connected: true } && _stream is not null &&
                string.Equals(_connectedIp, ip, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _readerCts?.Cancel();
            _stream?.Dispose();
            _client?.Dispose();

            var client = new TcpClient
            {
                NoDelay = true,
                SendBufferSize = 4096,
                ReceiveBufferSize = 4096
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
            await client.ConnectAsync(ip, Port, timeout.Token);

            _client = client;
            _stream = client.GetStream();
            _connectedIp = ip;
            _readerCts = new CancellationTokenSource();
            _readerTask = DrainResponsesAsync(_stream, _readerCts.Token);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static byte[] BuildIrccFrame(int irCode)
    {
        var parameters = irCode.ToString("D16", CultureInfo.InvariantCulture);
        var frame = $"*SCIRCC{parameters}\n";
        return Encoding.ASCII.GetBytes(frame);
    }

    private static async Task DrainResponsesAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
            }
        }
        catch
        {
            // The next write will reconnect if the socket has gone away.
        }
    }

    public void Dispose()
    {
        _readerCts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _readerCts?.Dispose();
        _connectionLock.Dispose();
        _writeLock.Dispose();
    }
}
