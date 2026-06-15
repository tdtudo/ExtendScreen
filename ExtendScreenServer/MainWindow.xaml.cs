using System.Windows;
using ExtendScreenServer.ViewModels;

namespace ExtendScreenServer;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private ClientDisplayWindow? _clientWindow;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;

        Closing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    private async void WifiMode_Click(object sender, RoutedEventArgs e)
    {
        ShowWorkView();
        await ViewModel.StartWifiModeAsync();
    }

    private async void UsbMode_Click(object sender, RoutedEventArgs e)
    {
        ShowWorkView();
        await ViewModel.StartUsbModeAsync();
    }

    private void ClientMode_Click(object sender, RoutedEventArgs e)
    {
        ShowClientView();
    }

    private void BackToModeSelect_Click(object sender, RoutedEventArgs e)
    {
        // 如果在客户端模式，断开客户端
        _clientWindow?.Disconnect();
        _clientWindow = null;

        ViewModel.StopServices();
        ShowModeSelectView();
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Disconnect();
    }

    // 客户端模式事件
    private async void ClientConnect_Click(object sender, RoutedEventArgs e)
    {
        var host = ServerIpBox.Text.Trim();
        if (string.IsNullOrEmpty(host))
        {
            System.Windows.MessageBox.Show("请输入服务端 IP 地址");
            return;
        }

        if (!int.TryParse(ClientVideoPortBox.Text.Trim(), out var videoPort)) videoPort = 12348;
        if (!int.TryParse(ClientInputPortBox.Text.Trim(), out var inputPort)) inputPort = 12347;

        ClientConnectBtn.IsEnabled = false;
        ClientConnectBtn.Content = "连接中...";

        try
        {
            _clientWindow = new ClientDisplayWindow();
            _clientWindow.Disconnected += OnClientDisconnected;
            await _clientWindow.ConnectAsync(host, videoPort, inputPort);

            // 连接成功，更新状态
            ClientStatusBorder.Visibility = Visibility.Visible;
            ClientDisconnectBtn.Visibility = Visibility.Visible;
            ClientStatusDot.Fill = System.Windows.Media.Brushes.Green;
            ClientStatusText.Text = $"已连接 {host}";
            ClientConnectBtn.Content = "已连接";
            ClientConnectBtn.IsEnabled = true;

            // 显示视频窗口
            _clientWindow.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"连接失败: {ex.Message}");
            _clientWindow = null;
            ClientConnectBtn.IsEnabled = true;
            ClientConnectBtn.Content = "连接";
        }
    }

    private void OnClientDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            ClientStatusDot.Fill = System.Windows.Media.Brushes.Red;
            ClientStatusText.Text = "连接已断开";
            ClientConnectBtn.Content = "连接";
            ClientConnectBtn.IsEnabled = true;
        });
    }

    private void ClientDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _clientWindow?.Disconnect();
        _clientWindow = null;
        ClientStatusDot.Fill = System.Windows.Media.Brushes.Coral;
        ClientStatusText.Text = "已断开";
        ClientConnectBtn.Content = "连接";
        ClientConnectBtn.IsEnabled = true;
    }

    private void ShowWorkView()
    {
        ModeSelectView.Visibility = Visibility.Collapsed;
        WorkView.Visibility = Visibility.Visible;
        ClientView.Visibility = Visibility.Collapsed;
    }

    private void ShowClientView()
    {
        ModeSelectView.Visibility = Visibility.Collapsed;
        WorkView.Visibility = Visibility.Collapsed;
        ClientView.Visibility = Visibility.Visible;
    }

    private void ShowModeSelectView()
    {
        ModeSelectView.Visibility = Visibility.Visible;
        WorkView.Visibility = Visibility.Collapsed;
        ClientView.Visibility = Visibility.Collapsed;
    }
}
