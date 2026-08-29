using System.Windows.Input;
using SonyBraviaControl.Infrastructure;
using SonyBraviaControl.Models;
using SonyBraviaControl.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace SonyBraviaControl.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IAdbService _adb;
    private readonly ISimpleIpControlService _simpleIp;
    private readonly IIrccService _ircc;
    private readonly IWakeOnLanService _wakeOnLan;
    private readonly ISettingsStore _settingsStore;
    private readonly AsyncRelayCommand _connectCommand;
    private readonly AsyncRelayCommand _disconnectCommand;
    private readonly AsyncRelayCommand<string> _sendKeyCommand;
    private readonly AsyncRelayCommand<string> _launchAppCommand;
    private readonly AsyncRelayCommand _sendTextCommand;
    private readonly AsyncRelayCommand _rebootCommand;

    private string _ipAddress = "192.168.1.2";
    private string _port = "5555";
    private string _macAddress = string.Empty;
    private string _adbPath = "adb.exe";
    private string _preSharedKey = string.Empty;
    private string _deviceName = "Sony Bravia · sin conectar";
    private string _statusText = "Desconectado";
    private Brush _statusBrush = Brushes.IndianRed;
    private string _textToSend = string.Empty;
    private bool _isConnected;
    private bool _isSimpleIpControlAvailable;
    private bool _isIpControlAvailable;

    public MainViewModel(
        IAdbService adb,
        ISimpleIpControlService simpleIp,
        IIrccService ircc,
        IWakeOnLanService wakeOnLan,
        ISettingsStore settingsStore)
    {
        _adb = adb;
        _simpleIp = simpleIp;
        _ircc = ircc;
        _wakeOnLan = wakeOnLan;
        _settingsStore = settingsStore;

        _connectCommand = new AsyncRelayCommand(ConnectAsync);
        _disconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        _sendKeyCommand = new AsyncRelayCommand<string>(
            SendKeyAsync,
            _ => IsConnected,
            allowConcurrentExecutions: true);
        _launchAppCommand = new AsyncRelayCommand<string>(LaunchAppAsync, _ => IsConnected);
        _sendTextCommand = new AsyncRelayCommand(SendTextAsync, () => IsConnected && !string.IsNullOrWhiteSpace(TextToSend));
        _rebootCommand = new AsyncRelayCommand(RebootAsync, () => IsConnected);
        WakeCommand = new AsyncRelayCommand(WakeAsync);
    }

    public string IpAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }
    public string Port { get => _port; set => SetProperty(ref _port, value); }
    public string MacAddress { get => _macAddress; set => SetProperty(ref _macAddress, value); }
    public string AdbPath { get => _adbPath; set => SetProperty(ref _adbPath, value); }
    public string PreSharedKey { get => _preSharedKey; set => SetProperty(ref _preSharedKey, value); }
    public string DeviceName { get => _deviceName; private set => SetProperty(ref _deviceName, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public Brush StatusBrush { get => _statusBrush; private set => SetProperty(ref _statusBrush, value); }

    public string TextToSend
    {
        get => _textToSend;
        set
        {
            if (SetProperty(ref _textToSend, value))
                _sendTextCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetProperty(ref _isConnected, value)) return;
            _disconnectCommand.RaiseCanExecuteChanged();
            _sendKeyCommand.RaiseCanExecuteChanged();
            _launchAppCommand.RaiseCanExecuteChanged();
            _sendTextCommand.RaiseCanExecuteChanged();
            _rebootCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand ConnectCommand => _connectCommand;
    public ICommand DisconnectCommand => _disconnectCommand;
    public ICommand SendKeyCommand => _sendKeyCommand;
    public ICommand LaunchAppCommand => _launchAppCommand;
    public ICommand WakeCommand { get; }
    public ICommand SendTextCommand => _sendTextCommand;
    public ICommand RebootCommand => _rebootCommand;

    private string Serial => $"{IpAddress.Trim()}:{(int.TryParse(Port, out var port) ? port : 5555)}";

    public async Task InitializeAsync()
    {
        var settings = _settingsStore.Load();
        IpAddress = settings.IpAddress;
        Port = settings.Port.ToString();
        MacAddress = settings.MacAddress;
        AdbPath = _adb.ResolveAdbPath(settings.AdbPath);
        PreSharedKey = settings.PreSharedKey;

        if (settings.AutoConnect)
            await ConnectAsync();
    }

    public void SaveCurrentSettings() => SaveSettings();

    private async Task ConnectAsync()
    {
        try
        {
            SetBusy("Conectando…");
            AdbPath = _adb.ResolveAdbPath(AdbPath);
            SaveSettings();

            _isSimpleIpControlAvailable = await _simpleIp.ConnectAsync(IpAddress);
            _isIpControlAvailable = !_isSimpleIpControlAvailable &&
                                    await _ircc.ProbeAsync(IpAddress, PreSharedKey);

            await _adb.ConnectAsync(AdbPath, Serial);
            if (!await _adb.IsConnectedAsync(AdbPath, Serial))
            {
                if (!_isSimpleIpControlAvailable && !_isIpControlAvailable)
                {
                    SetDisconnected("Sin conexión");
                    return;
                }
            }

            var model = await _adb.GetModelAsync(AdbPath, Serial);
            DeviceName = string.IsNullOrWhiteSpace(model) ? $"Sony Bravia · {Serial}" : $"{model} · {Serial}";
            IsConnected = true;
            SetConnectedStatus();
        }
        catch
        {
            if (_isSimpleIpControlAvailable || _isIpControlAvailable)
            {
                IsConnected = true;
                DeviceName = $"Sony Bravia · {IpAddress.Trim()}";
                SetConnectedStatus();
                return;
            }

            SetDisconnected("Error de conexión");
        }
    }

    private async Task DisconnectAsync()
    {
        _isSimpleIpControlAvailable = false;
        _isIpControlAvailable = false;
        await _simpleIp.DisconnectAsync();
        try { await _adb.DisconnectAsync(AdbPath, Serial); }
        finally { SetDisconnected("Desconectado"); }
    }

    private async Task SendKeyAsync(string? keyCode)
    {
        if (string.IsNullOrWhiteSpace(keyCode)) return;

        try
        {
            if (_isSimpleIpControlAvailable && _simpleIp.SupportsKey(keyCode))
            {
                if (await _simpleIp.SendKeyAsync(IpAddress, keyCode))
                    return;

                _isSimpleIpControlAvailable = false;
                _isIpControlAvailable = await _ircc.ProbeAsync(IpAddress, PreSharedKey);
                SetConnectedStatus();
            }

            if (_isIpControlAvailable && _ircc.SupportsKey(keyCode))
            {
                if (await _ircc.SendKeyAsync(IpAddress, PreSharedKey, keyCode))
                    return;

                _isIpControlAvailable = false;
                SetConnectedStatus();
            }

            await _adb.SendKeyAsync(AdbPath, Serial, keyCode);
        }
        catch
        {
            SetDisconnected("Conexión perdida");
        }
    }

    private async Task LaunchAppAsync(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)) return;
        try { await _adb.LaunchPackageAsync(AdbPath, Serial, packageName); }
        catch { StatusText = "ADB no disponible para apps"; StatusBrush = Brushes.Goldenrod; }
    }

    private async Task SendTextAsync()
    {
        if (string.IsNullOrWhiteSpace(TextToSend)) return;
        try
        {
            await _adb.SendTextAsync(AdbPath, Serial, TextToSend);
            TextToSend = string.Empty;
        }
        catch { StatusText = "ADB no disponible para texto"; StatusBrush = Brushes.Goldenrod; }
    }

    private async Task WakeAsync()
    {
        try
        {
            AdbPath = _adb.ResolveAdbPath(AdbPath);
            SaveSettings();

            if (await _adb.IsConnectedAsync(AdbPath, Serial))
            {
                IsConnected = true;
                await _adb.SendKeyAsync(AdbPath, Serial, "KEYCODE_WAKEUP");
                await ProbeRemotePathsAsync();
                SetConnectedStatus();
                return;
            }

            if (string.IsNullOrWhiteSpace(MacAddress))
            {
                SetDisconnected("Falta la MAC");
                return;
            }

            SetBusy("Encendiendo…");
            await _wakeOnLan.WakeAsync(MacAddress);

            for (var attempt = 0; attempt < 15; attempt++)
            {
                await Task.Delay(1000);
                try
                {
                    if (await _simpleIp.ConnectAsync(IpAddress))
                    {
                        _isSimpleIpControlAvailable = true;
                        IsConnected = true;
                        SetConnectedStatus();
                        return;
                    }

                    await _adb.ConnectAsync(AdbPath, Serial);
                    if (!await _adb.IsConnectedAsync(AdbPath, Serial)) continue;

                    var model = await _adb.GetModelAsync(AdbPath, Serial);
                    DeviceName = string.IsNullOrWhiteSpace(model) ? $"Sony Bravia · {Serial}" : $"{model} · {Serial}";
                    IsConnected = true;
                    await _adb.SendKeyAsync(AdbPath, Serial, "KEYCODE_WAKEUP");
                    await ProbeRemotePathsAsync();
                    SetConnectedStatus();
                    return;
                }
                catch { }
            }

            SetDisconnected("Encendida, esperando red");
        }
        catch { SetDisconnected("No se pudo encender"); }
    }

    private async Task RebootAsync()
    {
        try
        {
            StatusText = "Reiniciando…";
            StatusBrush = Brushes.Goldenrod;
            _isSimpleIpControlAvailable = false;
            _isIpControlAvailable = false;
            await _simpleIp.DisconnectAsync();
            await _adb.RebootAsync(AdbPath, Serial);
            IsConnected = false;
        }
        catch { SetDisconnected("Error al reiniciar"); }
    }

    private async Task ProbeRemotePathsAsync()
    {
        _isSimpleIpControlAvailable = await _simpleIp.ConnectAsync(IpAddress);
        _isIpControlAvailable = !_isSimpleIpControlAvailable &&
                                await _ircc.ProbeAsync(IpAddress, PreSharedKey);
    }

    private void SaveSettings()
    {
        _settingsStore.Save(new BraviaSettings
        {
            IpAddress = IpAddress.Trim(),
            Port = int.TryParse(Port, out var port) ? port : 5555,
            MacAddress = MacAddress.Trim(),
            AdbPath = AdbPath,
            PreSharedKey = PreSharedKey.Trim(),
            AutoConnect = true
        });
    }

    private void SetConnectedStatus()
    {
        if (_isSimpleIpControlAvailable)
        {
            StatusText = "TCP directo · 20060";
            StatusBrush = Brushes.LimeGreen;
        }
        else if (_isIpControlAvailable)
        {
            StatusText = "IRCC HTTP";
            StatusBrush = Brushes.Goldenrod;
        }
        else
        {
            StatusText = "ADB";
            StatusBrush = Brushes.DarkOrange;
        }
    }

    private void SetBusy(string text)
    {
        StatusText = text;
        StatusBrush = Brushes.Goldenrod;
    }

    private void SetDisconnected(string text)
    {
        _isSimpleIpControlAvailable = false;
        _isIpControlAvailable = false;
        IsConnected = false;
        StatusText = text;
        StatusBrush = Brushes.IndianRed;
        DeviceName = $"Sony Bravia · {Serial}";
    }
}
