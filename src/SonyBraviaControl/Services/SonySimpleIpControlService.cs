using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace SonyBraviaControl.Services;

public sealed class SonySimpleIpControlService : ISimpleIpControlService, IDisposable
{
    private const int Port = 20060;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromMilliseconds(800);

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
    private bool _disposed;

    public bool SupportsKey(string androidKeyCode) => IrCodes.ContainsKey(androidKeyCode);

    public async Task<bool> ConnectAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return false;

        try
        {
            await EnsureConnectedAsync(ipAddress, cancellationToken);
            return IsConnectionUsable(ipAddress.Trim());
        }
        catch
        {
            await DisconnectAsync();
            return false;
        }
    }

    public async Task<bool> SendKeyAsync(string ipAddress, string androidKeyCode, CancellationToken cancellationToken = default)
    {
        if (_disposed || !IrCodes.TryGetValue(androidKeyCode, out var irCode))
            return false;

        var frame = BuildIrccFrame(irCode);

        // A Sony TV can silently drop the persistent Simple IP socket after it has
        // been idle for a while. Retry once on a fresh connection so the first key
        // pressed after a long idle period still works instead of leaving the remote dead.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            NetworkStream? stream = null;
            try
            {
                await EnsureConnectedAsync(ipAddress, cancellationToken);
                stream = _stream;
                if (stream is null || !IsConnectionUsable(ipAddress.Trim()))
                    throw new IOException("La conexión Simple IP ya no está disponible.");

                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeout.CancelAfter(SendTimeout);

                await _writeLock.WaitAsync(sendTimeout.Token);
                try
                {
                    // Never use the field after acquiring the write lock: another task may
                    // have replaced the connection while this command was waiting.
                    if (!ReferenceEquals(stream, _stream) || !IsConnectionUsable(ipAddress.Trim()))
                        throw new IOException("La conexión Simple IP cambió antes del envío.");

                    await stream.WriteAsync(frame, sendTimeout.Token);
                }
                finally
                {
                    _writeLock.Release();
                }

                return true;
            }
            catch
            {
                await InvalidateConnectionAsync(stream);
                if (attempt == 0 && !cancellationToken.IsCancellationRequested)
                    continue;
            }
        }

        return false;
    }

    public async Task DisconnectAsync()
    {
        if (_disposed)
            return;

        await _connectionLock.WaitAsync();
        try
        {
            DisposeConnectionCore();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task EnsureConnectedAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var ip = ipAddress.Trim();
        if (IsConnectionUsable(ip))
            return;

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnectionUsable(ip))
                return;

            DisposeConnectionCore();

            var client = new TcpClient
            {
                NoDelay = true,
                SendBufferSize = 4096,
                ReceiveBufferSize = 4096
            };

            // Ask Windows to detect a broken idle TCP connection instead of allowing a
            // half-open socket to live forever. We still do our own stale-socket check too.
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            catch
            {
                // Keep-alive is an optimisation; explicit reconnect logic remains enough.
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
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

    private bool IsConnectionUsable(string ipAddress)
    {
        var client = _client;
        if (client is null || _stream is null || !client.Connected ||
            !string.Equals(_connectedIp, ipAddress, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var socket = client.Client;
            // Connected only reports the state of the last socket operation. Poll +
            // Available detects a graceful close that happened while the app was idle.
            return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch
        {
            return false;
        }
    }

    private async Task DrainResponsesAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[256];
        var connectionLost = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    connectionLost = true;
                    break;
                }
            }
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            connectionLost = true;
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown/reconnect.
        }

        if (connectionLost)
            await InvalidateConnectionAsync(stream);
    }

    private async Task InvalidateConnectionAsync(NetworkStream? observedStream)
    {
        if (_disposed)
            return;

        await _connectionLock.WaitAsync();
        try
        {
            // Do not tear down a newer socket created by another concurrent key press.
            if (observedStream is not null && !ReferenceEquals(_stream, observedStream))
                return;

            DisposeConnectionCore();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void DisposeConnectionCore()
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

    private static byte[] BuildIrccFrame(int irCode)
    {
        var parameters = irCode.ToString("D16", CultureInfo.InvariantCulture);
        var frame = $"*SCIRCC{parameters}\n";
        return Encoding.ASCII.GetBytes(frame);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _readerCts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _readerCts?.Dispose();
        _connectionLock.Dispose();
        _writeLock.Dispose();
    }
}
