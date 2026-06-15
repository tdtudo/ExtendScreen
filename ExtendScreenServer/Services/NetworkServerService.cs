using System.Net;
using System.Net.Sockets;
using ExtendScreenServer.Models;

namespace ExtendScreenServer.Services;

/// <summary>
/// 网络传输服务 — 发送编码后的视频帧，接收触控输入
/// </summary>
public class NetworkServerService : IDisposable
{
    private readonly ServerConfig _config;
    private TcpListener? _inputListener;
    private TcpClient? _inputClient;
    private NetworkStream? _inputStream;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public event Action<InputEvent>? InputEventReceived;

    public bool IsConnected => _inputClient?.Connected == true;

    public NetworkServerService(ServerConfig config)
    {
        _config = config;
    }

    public async Task StartAsync()
    {
        _isRunning = true;
        _cts = new CancellationTokenSource();

        // 只监听输入端口，视频端口由 StreamingService 负责
        _inputListener = new TcpListener(IPAddress.Any, _config.InputEventPort);
        _inputListener.Start();

        // 接受输入连接
        _ = Task.Run(async () =>
        {
            try
            {
                _inputClient = await _inputListener!.AcceptTcpClientAsync(_cts.Token);
                _inputStream = _inputClient.GetStream();
                System.Diagnostics.Debug.WriteLine("Input client connected.");
                _ = Task.Run(ReceiveInputLoop, _cts.Token);
            }
            catch (OperationCanceledException) { }
        }, _cts.Token);
    }

    /// <summary>
    /// 接收安卓端触控输入
    /// </summary>
    private async Task ReceiveInputLoop()
    {
        var buffer = new byte[1024];
        while (!_cts!.IsCancellationRequested && _inputClient?.Connected == true)
        {
            try
            {
                var bytesRead = await _inputStream!.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (bytesRead == 0) break;

                var inputEvent = InputEvent.Deserialize(buffer.AsSpan(0, bytesRead));
                if (inputEvent != null)
                {
                    InputEventReceived?.Invoke(inputEvent);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Input receive error: {ex.Message}");
                break;
            }
        }
    }

    /// <summary>
    /// 模拟触控输入到Windows系统
    /// </summary>
    public void InjectInput(InputEvent input)
    {
        InputInjector.Inject(input);
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _inputStream?.Close();
        _inputClient?.Close();
        _inputListener?.Stop();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
