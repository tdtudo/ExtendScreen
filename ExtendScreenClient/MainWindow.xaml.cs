using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ExtendScreenClient;

public partial class MainWindow : Window
{
    private VideoReceiver? _videoReceiver;
    private InputSender? _inputSender;
    private bool _isMouseDown;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        var host = IpBox.Text.Trim();
        if (string.IsNullOrEmpty(host))
        {
            MessageBox.Show("请输入服务端 IP 地址");
            return;
        }

        if (!int.TryParse(VideoPortBox.Text.Trim(), out var videoPort)) videoPort = 12348;
        if (!int.TryParse(InputPortBox.Text.Trim(), out var inputPort)) inputPort = 12347;

        ConnectBtn.IsEnabled = false;
        ConnectBtn.Content = "连接中...";

        try
        {
            _videoReceiver = new VideoReceiver();
            _videoReceiver.FrameReceived += OnFrameReceived;
            _videoReceiver.ConnectionChanged += OnVideoConnectionChanged;

            _inputSender = new InputSender();

            // 同时连接视频和输入
            var videoTask = _videoReceiver.ConnectAsync(host, videoPort);
            var inputTask = _inputSender.ConnectAsync(host, inputPort);
            await Task.WhenAll(videoTask, inputTask);

            // 切换到视频显示界面
            ConnectPanel.Visibility = Visibility.Collapsed;
            VideoPanel.Visibility = Visibility.Visible;
            StatusText.Text = $"已连接 {host}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"连接失败: {ex.Message}");
            _videoReceiver?.Dispose();
            _inputSender?.Dispose();
            _videoReceiver = null;
            _inputSender = null;
            ConnectBtn.IsEnabled = true;
            ConnectBtn.Content = "连接";
        }
    }

    private void OnFrameReceived(BitmapSource bitmap)
    {
        Dispatcher.Invoke(() =>
        {
            VideoImage.Source = bitmap;
        });
    }

    private void OnVideoConnectionChanged(bool connected)
    {
        Dispatcher.Invoke(() =>
        {
            if (!connected)
            {
                StatusText.Text = "连接已断开";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                StatusText.Text = "已连接";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#a6e3a1"));
            }
        });
    }

    private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        Disconnect();
    }

    private void Disconnect()
    {
        _videoReceiver?.Dispose();
        _inputSender?.Dispose();
        _videoReceiver = null;
        _inputSender = null;

        Dispatcher.Invoke(() =>
        {
            VideoPanel.Visibility = Visibility.Collapsed;
            ConnectPanel.Visibility = Visibility.Visible;
            ConnectBtn.IsEnabled = true;
            ConnectBtn.Content = "连接";
            VideoImage.Source = null;
        });
    }

    // 鼠标事件 → 发送到服务端
    private void VideoImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_inputSender?.IsConnected != true) return;
        var (nx, ny) = GetNormalizedPos(e);
        _isMouseDown = true;
        _inputSender.SendTouch(nx, ny, 0); // action=0 down
    }

    private void VideoImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_inputSender?.IsConnected != true) return;
        var (nx, ny) = GetNormalizedPos(e);
        _isMouseDown = false;
        _inputSender.SendTouch(nx, ny, 2); // action=2 up
    }

    private void VideoImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_inputSender?.IsConnected != true || !_isMouseDown) return;
        var (nx, ny) = GetNormalizedPos(e);
        _inputSender.SendTouch(nx, ny, 1); // action=1 move
    }

    private void VideoImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_inputSender?.IsConnected != true) return;
        var delta = e.Delta / 120f;
        _inputSender.SendScroll(0, delta);
    }

    private (float x, float y) GetNormalizedPos(MouseEventArgs e)
    {
        var pos = e.GetPosition(VideoImage);
        var x = (float)(pos.X / VideoImage.ActualWidth);
        var y = (float)(pos.Y / VideoImage.ActualHeight);
        return (Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f));
    }

    protected override void OnClosed(EventArgs e)
    {
        Disconnect();
        base.OnClosed(e);
    }
}
