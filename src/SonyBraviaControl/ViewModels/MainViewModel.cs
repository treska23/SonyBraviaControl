using System.Windows.Input;
using System.Windows.Media;
using SonyBraviaControl.Infrastructure;
using SonyBraviaControl.Models;
using SonyBraviaControl.Services;

namespace SonyBraviaControl.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IAdbService _adb;
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
    private string _deviceName = "Sony Bravia · sin conectar";
    private string _statusText = "Desconectado";
    private Brush _statusBrush = Brushes.IndianRed;
    private string _textToSend = string.Empty;
    private bool _isConnected;

    public MainViewModel(IAdbService adb, IWakeOnLanService wakeOnLan, ISettingsStore settingsStore)
    {
        _adb = adb;
        _wakeOnLan = wakeOnLan;
        _settingsStore = settingsStore;

        _connectCommand = new AsyncRelayCommand(ConnectAsync);
        _disconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        _sendKeyCommand = new AsyncRelayCommand<string>(SendKeyAsync, _ => IsConnected);
        _launchAppCommand = new AsyncRelayCommand<string>(LaunchAppAsync, _ => IsConnected);
        _sendTextCommand = new AsyncRelayCommand(SendTextAsync, () => IsConnected && !string.IsNullOrWhiteSpace(TextToSend));
        _rebootCommand = new AsyncRelayCommand(RebootAsync, () => IsConnected);
        WakeCommand = new AsyncRelayCommand(WakeAsync);
    }

    public string IpAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }
    public string Port { get => _port; set => SetProperty(ref _port, value); }
    public string MacAddress { get => _macAddress; set => SetProperty(ref _macAddress, value); }
    public string AdbPath { get => _adbPath; set => SetProperty(ref _adbPath, value); }
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

        if (settings.AutoConnect)
            await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        try
        {
            SetBusy("Conectando…");
            AdbPath = _adb.ResolveAdbPath(AdbPath);
            SaveSettings();
            await _adb.ConnectAsync(AdbPath, Serial);

            if (!await _adb.IsConnectedAsync(AdbPath, Serial))
            {
                SetDisconnected("Sin conexión");
                return;
            }

            var model = await _adb.GetModelAsync(AdbPath, Serial);
            DeviceName = string.IsNullOrWhiteSpace(model) ? $"Sony Bravia · {Serial}" : $"{model} · {Serial}";
            IsConnected = true;
            StatusText = "Conectado";
            StatusBrush = Brushes.MediumAquamarine;
        }
        catch
        {
            SetDisconnected("Error ADB");
        }
    }

    private async Task DisconnectAsync()
    {
        try { await _adb.DisconnectAsync(AdbPath, Serial); }
        finally { SetDisconnected("Desconectado"); }
    }

    private async Task SendKeyAsync(string? keyCode)
    {
        if (string.IsNullOrWhiteSpace(keyCode)) return;
        try { await _adb.SendKeyAsync(AdbPath, Serial, keyCode); }
        catch { SetDisconnected("Conexión perdida"); }
    }

    private async Task LaunchAppAsync(string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName)) return;
        try { await _adb.LaunchPackageAsync(AdbPath, Serial, packageName); }
        catch { SetDisconnected("Conexión perdida"); }
    }

    private async Task SendTextAsync()
    {
        if (string.IsNullOrWhiteSpace(TextToSend)) return;
        try
        {
            await _adb.SendTextAsync(AdbPath, Serial, TextToSend);
            TextToSend = string.Empty;
        }
        catch { SetDisconnected("Conexión perdida"); }
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
                StatusText = "Conectado";
                StatusBrush = Brushes.MediumAquamarine;
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
                    await _adb.ConnectAsync(AdbPath, Serial);
                    if (!await _adb.IsConnectedAsync(AdbPath, Serial)) continue;

                    var model = await _adb.GetModelAsync(AdbPath, Serial);
                    DeviceName = string.IsNullOrWhiteSpace(model) ? $"Sony Bravia · {Serial}" : $"{model} · {Serial}";
                    IsConnected = true;
                    StatusText = "Conectado";
                    StatusBrush = Brushes.MediumAquamarine;
                    await _adb.SendKeyAsync(AdbPath, Serial, "KEYCODE_WAKEUP");
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
            await _adb.RebootAsync(AdbPath, Serial);
            IsConnected = false;
        }
        catch { SetDisconnected("Error al reiniciar"); }
    }

    private void SaveSettings()
    {
        _settingsStore.Save(new BraviaSettings
        {
            IpAddress = IpAddress.Trim(),
            Port = int.TryParse(Port, out var port) ? port : 5555,
            MacAddress = MacAddress.Trim(),
            AdbPath = AdbPath,
            AutoConnect = true
        });
    }

    private void SetBusy(string text)
    {
        StatusText = text;
        StatusBrush = Brushes.Goldenrod;
    }

    private void SetDisconnected(string text)
    {
        IsConnected = false;
        StatusText = text;
        StatusBrush = Brushes.IndianRed;
        DeviceName = $"Sony Bravia · {Serial}";
    }
}
