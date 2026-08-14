using System.Reflection;
using System.Windows.Forms;
using FailSharp.Core;

namespace FailSharp;

/// <summary>System tray presence: opens the settings window on click, or when the in-sim menu item is clicked.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;
    private readonly FailureEngine _engine;
    private readonly ToolStripMenuItem _enabledItem;

    public TrayIcon(MainWindow mainWindow, NativeControlClient menuClient, FailureEngine engine)
    {
        _mainWindow = mainWindow;
        _engine = engine;

        _enabledItem = new ToolStripMenuItem("Enabled")
        {
            CheckOnClick = true,
            Checked = engine.Settings.Enabled,
        };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            _engine.Settings.Enabled = _enabledItem.Checked;
            SettingsStore.Save(_engine.Settings);
            _mainWindow.Dispatcher.BeginInvoke(() => _mainWindow.SetEnabled(_enabledItem.Checked));
        };
        _mainWindow.EnabledChanged += (_, enabled) => _enabledItem.Checked = enabled;

        var alwaysOnTopItem = new ToolStripMenuItem("Always on top")
        {
            CheckOnClick = true,
            Checked = engine.Settings.AlwaysOnTop,
        };
        alwaysOnTopItem.CheckedChanged += (_, _) =>
        {
            _engine.Settings.AlwaysOnTop = alwaysOnTopItem.Checked;
            _mainWindow.Dispatcher.BeginInvoke(() => _mainWindow.Topmost = alwaysOnTopItem.Checked);
            SettingsStore.Save(_engine.Settings);
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Settings", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add(_enabledItem);
        contextMenu.Items.Add(alwaysOnTopItem);
        contextMenu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "FailSharp",
            ContextMenuStrip = contextMenu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        menuClient.MenuClicked += (_, _) => _mainWindow.Dispatcher.BeginInvoke(ShowMainWindow);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FailSharp.Resources.tray-icon.ico");
        return stream is not null ? new System.Drawing.Icon(stream) : System.Drawing.SystemIcons.Application;
    }

    public void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = System.Windows.WindowState.Normal;
        _mainWindow.Activate();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
