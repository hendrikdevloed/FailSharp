using System.Windows;
using System.Windows.Forms;

namespace FailSharp;

/// <summary>System tray presence: opens the settings window on click, or when the in-sim menu item is clicked.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Window _mainWindow;

    public TrayIcon(Window mainWindow, MenuClient menuClient)
    {
        _mainWindow = mainWindow;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Settings", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "FailSharp",
            ContextMenuStrip = contextMenu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        menuClient.MenuClicked += (_, _) => _mainWindow.Dispatcher.BeginInvoke(ShowMainWindow);
    }

    private void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
