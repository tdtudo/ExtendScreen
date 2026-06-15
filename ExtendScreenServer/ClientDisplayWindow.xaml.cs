using System.Net.Sockets;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ExtendScreenServer;

/// <summary>
/// 客户端视频显示窗口 — 接收服务端画面并显示，发送鼠标输入
/// </summary>
public partial class ClientDisplayWindow : Window
{
    private TcpClient? _videoClient;
    private NetworkStream? _videoStream;
    private TcpClient? _inputClient;
    private NetworkStream? _inputStream;
    private CancellationTokenSource? _cts;
    private bool _isMouseDown;

    public event Action? Disconnected;

    public ClientDisplayWindow()
    {
        InitializeComponent();
    }

    public async Task ConnectAsync(string host, int videoPort, int inputPort)
    {
        _cts = new CancellationTokenSource();

        _videoClient = new TcpClient { NoDelay = true, ReceiveBufferSize = 512 * 1024 };
        _inputClient = new TcpClient { NoDelay = true };

        await Task.WhenAll(
            _videoClient.ConnectAsync(host, videoPort),
            _inputClient.ConnectAsync(host, inputPort)
        );

        _videoStream = _videoClient.GetStream();
        _inputStream = _inputClient.GetStream();

        StatusText.Text = $"已连接 {host}";

        _ = Task.Run(ReceiveLoop, _cts.Token);
    }

    private async Task ReceiveLoop()
    {
        var lengthBuffer = new byte[4];
        const int maxFrameSize = 5_000_000;
        var frameBuffer = new byte[maxFrameSize];

        try
        {
            while (!_cts!.IsCancellationRequested && _videoClient?.Connected == true)
            {
                if (!await ReadFully(_videoStream!, lengthBuffer, 4)) break;

                var frameLength = (lengthBuffer[0] << 24) | (lengthBuffer[1] << 16) | (lengthBuffer[2] << 8) | lengthBuffer[3];
                if (frameLength <= 0 || frameLength > maxFrameSize) continue;

                if (!await ReadFully(_videoStream!, frameBuffer, frameLength)) break;

                using var ms = new MemoryStream(frameBuffer, 0, frameLength);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                Dispatcher.Invoke(() => { VideoImage.Source = bitmap; });
            }
        }
        catch { }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "连接已断开";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            });
        }
    }

    private static async Task<bool> ReadFully(NetworkStream stream, byte[] buffer, int length)
    {
        var totalRead = 0;
        while (totalRead < length)
        {
            var bytesRead = await stream.ReadAsync(buffer, totalRead, length - totalRead);
            if (bytesRead == 0) return false;
            totalRead += bytesRead;
        }
        return true;
    }

    // 鼠标事件 → 发送到服务端
    private void VideoImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isMouseDown = true;
        SendTouch(e, 0);
    }

    private void VideoImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isMouseDown = false;
        SendTouch(e, 2);
    }

    private void VideoImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isMouseDown) SendTouch(e, 1);
    }

    private void VideoImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_inputStream == null) return;
        var delta = e.Delta / 120f;
        Send($"Scroll|0|0|0|{delta}|1.0|2|1|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    private void SendTouch(System.Windows.Input.MouseEventArgs e, int action)
    {
        if (_inputStream == null) return;
        var pos = e.GetPosition(VideoImage);
        var x = Math.Clamp((float)(pos.X / VideoImage.ActualWidth), 0f, 1f);
        var y = Math.Clamp((float)(pos.Y / VideoImage.ActualHeight), 0f, 1f);
        Send($"Touch|{x}|{y}|0|0|1.0|1|{action}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    private void Send(string message)
    {
        try
        {
            var data = System.Text.Encoding.UTF8.GetBytes(message);
            _inputStream?.Write(data, 0, data.Length);
            _inputStream?.Flush();
        }
        catch { }
    }

    private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        Disconnect();
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _videoStream?.Close();
        _inputStream?.Close();
        _videoClient?.Close();
        _inputClient?.Close();
        Disconnected?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        Disconnect();
        base.OnClosed(e);
    }
}
