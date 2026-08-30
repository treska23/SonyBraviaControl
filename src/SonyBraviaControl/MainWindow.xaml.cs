using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SonyBraviaControl.Infrastructure;
using SonyBraviaControl.ViewModels;
using Forms = System.Windows.Forms;

namespace SonyBraviaControl;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Icon _applicationIcon;
    private readonly DispatcherTimer _globalRemoteTimeoutTimer;
    private GlobalRemoteControlService? _globalRemote;
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
            // The app is designed to live permanently in the tray, even while its
            // main window is visible.
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);

        _globalRemoteTimeoutTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _globalRemoteTimeoutTimer.Tick += (_, _) =>
        {
            _globalRemoteTimeoutTimer.Stop();
            _globalRemote?.Deactivate();
        };

        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (source is null)
            return;

        _globalRemote = new GlobalRemoteControlService(
            source,
            CanCaptureGlobalKey,
            OnGlobalRemoteKeyPressed);
        _globalRemote.ActiveChanged += OnGlobalRemoteModeChanged;

        if (!_globalRemote.Start())
        {
            ShowTrayMessage(
                "Atajo global no disponible",
                "No se pudo registrar Ctrl+Alt+Espacio. Otra aplicación puede estar usando esa combinación.");
        }
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // When the user is typing in a field, keyboard input must stay in that field.
        // Everywhere else in the window, the keyboard behaves like the TV remote.
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox)
            return;

        if (ExecuteRemoteKey(e.Key))
            e.Handled = true;
    }

    private bool CanCaptureGlobalKey(Key key)
    {
        var remoteKey = TranslateKeyboardKey(key);
        if (remoteKey is null || DataContext is not MainViewModel viewModel)
            return false;

        return viewModel.SendKeyCommand.CanExecute(remoteKey);
    }

    private void OnGlobalRemoteKeyPressed(Key key)
    {
        // A low-level keyboard hook must return immediately. Queue the network command
        // onto WPF's dispatcher instead of doing any TV work inside the hook callback.
        Dispatcher.BeginInvoke(() =>
        {
            ResetGlobalRemoteTimeout();
            ExecuteRemoteKey(key);
        });
    }

    private void OnGlobalRemoteModeChanged(bool active)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (active)
            {
                ResetGlobalRemoteTimeout();
                ShowTrayMessage(
                    "Modo mando activo",
                    "Flechas, Enter, Espacio, +/-, 1-4… controlan la TV. Ctrl+Alt+Espacio para salir.");
            }
            else
            {
                _globalRemoteTimeoutTimer.Stop();
                ShowTrayMessage(
                    "Modo mando desactivado",
                    "El teclado vuelve a funcionar normalmente en el PC.");
            }
        });
    }

    private void ResetGlobalRemoteTimeout()
    {
        _globalRemoteTimeoutTimer.Stop();
        _globalRemoteTimeoutTimer.Start();
    }

    private bool ExecuteRemoteKey(Key key)
    {
        var remoteKey = TranslateKeyboardKey(key);
        if (remoteKey is null || DataContext is not MainViewModel viewModel)
            return false;

        var command = viewModel.SendKeyCommand;
        if (!command.CanExecute(remoteKey))
            return false;

        command.Execute(remoteKey);
        return true;
    }

    private static string? TranslateKeyboardKey(Key key) => key switch
    {
        // Navigation: keyboard cursor keys mirror the remote D-pad.
        Key.Up => "KEYCODE_DPAD_UP",
        Key.Down => "KEYCODE_DPAD_DOWN",
        Key.Left => "KEYCODE_DPAD_LEFT",
        Key.Right => "KEYCODE_DPAD_RIGHT",
        Key.Enter => "KEYCODE_DPAD_CENTER",
        Key.Escape => "KEYCODE_BACK",
        Key.Back => "KEYCODE_BACK",
        Key.Home => "KEYCODE_HOME",
        Key.Apps => "KEYCODE_MENU",

        // Easy letter shortcuts while the remote window has focus/global mode is active.
        Key.H => "KEYCODE_HOME",
        Key.B => "KEYCODE_BACK",
        Key.M => "KEYCODE_MENU",
        Key.I => "KEYCODE_TV_INPUT",

        // Playback.
        Key.Space => "KEYCODE_MEDIA_PLAY_PAUSE",
        Key.MediaPlayPause => "KEYCODE_MEDIA_PLAY_PAUSE",
        Key.MediaStop => "KEYCODE_MEDIA_STOP",

        // Volume: both normal +/- and dedicated multimedia keys.
        Key.Add => "KEYCODE_VOLUME_UP",
        Key.OemPlus => "KEYCODE_VOLUME_UP",
        Key.VolumeUp => "KEYCODE_VOLUME_UP",
        Key.Subtract => "KEYCODE_VOLUME_DOWN",
        Key.OemMinus => "KEYCODE_VOLUME_DOWN",
        Key.VolumeDown => "KEYCODE_VOLUME_DOWN",
        Key.VolumeMute => "KEYCODE_VOLUME_MUTE",

        // Channels.
        Key.PageUp => "KEYCODE_CHANNEL_UP",
        Key.PageDown => "KEYCODE_CHANNEL_DOWN",

        // Direct HDMI shortcuts. Both number row and numeric keypad work.
        Key.D1 or Key.NumPad1 => "KEYCODE_TV_INPUT_HDMI_1",
        Key.D2 or Key.NumPad2 => "KEYCODE_TV_INPUT_HDMI_2",
        Key.D3 or Key.NumPad3 => "KEYCODE_TV_INPUT_HDMI_3",
        Key.D4 or Key.NumPad4 => "KEYCODE_TV_INPUT_HDMI_4",

        _ => null
    };

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveSettings();

        if (_allowExit)
        {
            _globalRemoteTimeoutTimer.Stop();
            _globalRemote?.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _applicationIcon.Dispose();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    internal void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        _trayIcon.Visible = true;
    }

    private void ShowFromTray()
    {
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

    private void ShowTrayMessage(string title, string message)
    {
        _trayIcon.ShowBalloonTip(1500, title, message, Forms.ToolTipIcon.Info);
    }

    private void SaveSettings()
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.SaveCurrentSettings();
    }
}
