using System.Net.Sockets;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ExtendScreenClient;

/// <summary>
/// 视频流接收器 — 连接服务端，接收 JPEG 帧并显示
/// </summary>
public class VideoReceiver : IDisposable
{
    private TcpClient? _videoClient;
    private NetworkStream? _videoStream;
    private CancellationTokenSource? _cts;
    private bool _isConnected;

    public event Action<BitmapSource>? FrameReceived;
    public event Action<bool>? ConnectionChanged;
    public bool IsConnected => _isConnected;

    public async Task ConnectAsync(string host, int port)
    {
        _cts = new CancellationTokenSource();
        _videoClient = new TcpClient();
        _videoClient.NoDelay = true;
        _videoClient.SendBufferSize = 8192;
        _videoClient.ReceiveBufferSize = 512 * 1024;

        try
        {
            await _videoClient.ConnectAsync(host, port, _cts.Token);
            _videoStream = _videoClient.GetStream();
            _isConnected = true;
            ConnectionChanged?.Invoke(true);

            _ = Task.Run(ReceiveLoop, _cts.Token);
        }
        catch
        {
            _isConnected = false;
            ConnectionChanged?.Invoke(false);
            throw;
        }
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
                // 读取 4 字节长度头
                if (!await ReadFully(_videoStream!, lengthBuffer, 4)) break;

                var frameLength = (lengthBuffer[0] << 24) | (lengthBuffer[1] << 16) | (lengthBuffer[2] << 8) | lengthBuffer[3];
                if (frameLength <= 0 || frameLength > maxFrameSize) continue;

                // 读取帧数据
                if (!await ReadFully(_videoStream!, frameBuffer, frameLength)) break;

                // 解码 JPEG
                using var ms = new MemoryStream(frameBuffer, 0, frameLength);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze(); // 跨线程使用

                FrameReceived?.Invoke(bitmap);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            _isConnected = false;
            ConnectionChanged?.Invoke(false);
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

    public void Disconnect()
    {
        _cts?.Cancel();
        _videoStream?.Close();
        _videoClient?.Close();
        _isConnected = false;
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
    }
}
