using System.Windows;

namespace ExtendScreenServer;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 初始化系统托盘图标
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "ExtendScreen Server"
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("设置", null, (s, args) => ShowMainWindow());
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add("退出", null, (s, args) => Shutdown());
        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (s, args) => ShowMainWindow();

        // 创建主窗口，首次启动时显示设置面板
        _mainWindow = new MainWindow();
        _mainWindow.Show();

        // 显示气泡提示，告知用户程序在托盘运行
        _trayIcon.ShowBalloonTip(3000, "ExtendScreen",
            "程序已在系统托盘中运行。等待设备连接...",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _mainWindow?.ViewModel?.Shutdown();
        base.OnExit(e);
    }
}
