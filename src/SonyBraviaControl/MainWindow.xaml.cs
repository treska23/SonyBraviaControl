using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SonyBraviaControl.Infrastructure;
using SonyBraviaControl.ViewModels;
using Forms = System.Windows.Forms;

namespace SonyBraviaControl;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Icon _applicationIcon;
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();

        _applicationIcon = AppIconFactory.CreateIcon();
        var windowIcon = Imaging.CreateBitmapSourceFromHIcon(
            _applicationIcon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        windowIcon.Freeze();
        Icon = windowIcon;

        var openItem = new Forms.ToolStripMenuItem("Abrir Sony Bravia Control");
        openItem.Click += (_, _) => Dispatcher.Invoke(ShowFromTray);

        var exitItem = new Forms.ToolStripMenuItem("Salir completamente");
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitApplication);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Sony Bravia Control",
            Icon = _applicationIcon,
            ContextMenuStrip = menu,
            Visible = false
        };

        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveSettings();

        if (_allowExit)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _applicationIcon.Dispose();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        _trayIcon.Visible = true;
    }

    private void ShowFromTray()
    {
        _trayIcon.Visible = false;
        ShowInTaskbar = true;
        Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
    }

    private void ExitApplication()
    {
        SaveSettings();
        _allowExit = true;
        Close();
    }

    private void SaveSettings()
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.SaveCurrentSettings();
    }
}
