using System.Windows;
using SonyBraviaControl.Infrastructure;
using SonyBraviaControl.Services;
using SonyBraviaControl.ViewModels;

namespace SonyBraviaControl;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StartupRegistration.EnsureRegistered();
        var startHidden = e.Args.Any(arg =>
            string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));

        var adbService = new AdbService();
        var simpleIpControlService = new SonySimpleIpControlService();
        var irccService = new SonyIrccService();
        var wakeOnLanService = new WakeOnLanService();
        var settingsStore = new UserSettingsStore();
        var viewModel = new MainViewModel(
            adbService,
            simpleIpControlService,
            irccService,
            wakeOnLanService,
            settingsStore);

        var window = new MainWindow
        {
            DataContext = viewModel
        };
        window.SourceInitialized += (_, _) => SonyWindowChrome.Apply(window);

        // Show once so WPF creates the native HWND. That keeps the global hotkey alive
        // even if Windows started us directly into the system tray.
        window.Show();
        if (startHidden)
            window.HideToTray();

        _ = viewModel.InitializeAsync();
    }
}
