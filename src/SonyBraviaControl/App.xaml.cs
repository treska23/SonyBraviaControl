using System.Windows;
using SonyBraviaControl.Services;
using SonyBraviaControl.ViewModels;

namespace SonyBraviaControl;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        window.Show();
        _ = viewModel.InitializeAsync();
    }
}
