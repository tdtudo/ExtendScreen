using System.Net.Sockets;
using System.IO;

namespace ExtendScreenClient;

/// <summary>
/// 输入事件发送器 — 将鼠标/键盘事件发送到服务端
/// 协议格式：Type|X|Y|DeltaX|DeltaY|Scale|PointerCount|Action|Timestamp（UTF8）
/// </summary>
public class InputSender : IDisposable
{
    private TcpClient? _inputClient;
    private NetworkStream? _inputStream;
    private bool _isConnected;

    public bool IsConnected => _isConnected;

    public async Task ConnectAsync(string host, int port)
    {
        _inputClient = new TcpClient();
        _inputClient.NoDelay = true;
        await _inputClient.ConnectAsync(host, port);
        _inputStream = _inputClient.GetStream();
        _isConnected = true;
    }

    public void SendTouch(float x, float y, int action)
    {
        Send($"Touch|{x}|{y}|0|0|1.0|1|{action}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    public void SendScroll(float deltaX, float deltaY)
    {
        Send($"Scroll|0|0|{deltaX}|{deltaY}|1.0|2|1|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    private void Send(string message)
    {
        if (!_isConnected || _inputStream == null) return;
        try
        {
            var data = System.Text.Encoding.UTF8.GetBytes(message);
            _inputStream.Write(data, 0, data.Length);
            _inputStream.Flush();
        }
        catch { }
    }

    public void Disconnect()
    {
        _inputStream?.Close();
        _inputClient?.Close();
        _isConnected = false;
    }

    public void Dispose()
    {
        Disconnect();
    }
}
