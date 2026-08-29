using System.Windows;
using SonyBraviaControl.Services;
using SonyBraviaControl.ViewModels;

namespace SonyBraviaControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var adbService = new AdbService();
        var wakeOnLanService = new WakeOnLanService();
        var settingsStore = new UserSettingsStore();
        var viewModel = new MainViewModel(adbService, wakeOnLanService, settingsStore);

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        window.Show();
        _ = viewModel.InitializeAsync();
    }
}
