using System.Net;
using System.Net.Sockets;
using System.Text;
using ExtendScreenServer.Models;

namespace ExtendScreenServer.Services;

/// <summary>
/// UDP广播服务发现 — 让安卓端自动发现电脑
/// </summary>
public class DiscoveryService : IDisposable
{
    private readonly ServerConfig _config;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public event Action<DeviceInfo>? DeviceConnected;

    public DiscoveryService(ServerConfig config)
    {
        _config = config;
    }

    public void Start()
    {
        _isRunning = true;
        _cts = new CancellationTokenSource();
        _udpClient = new UdpClient();
        _udpClient.EnableBroadcast = true;

        // 广播线程
        _ = Task.Run(BroadcastLoop, _cts.Token);
        // 监听回复线程
        _ = Task.Run(ListenForResponses, _cts.Token);
    }

    private async Task BroadcastLoop()
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, _config.DiscoveryPort);
        var message = Encoding.UTF8.GetBytes(_config.BroadcastMessage);

        while (!_cts!.IsCancellationRequested)
        {
            try
            {
                await _udpClient!.SendAsync(message, message.Length, endpoint);
                await Task.Delay(_config.BroadcastIntervalMs, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Broadcast error: {ex.Message}");
            }
        }
    }

    private async Task ListenForResponses()
    {
        // 监听安卓端的广播和设备信息
        var listen = new UdpClient(new IPEndPoint(IPAddress.Any, _config.DiscoveryPort + 1));

        while (!_cts!.IsCancellationRequested)
        {
            try
            {
                var result = await listen.ReceiveAsync(_cts.Token);
                var data = Encoding.UTF8.GetString(result.Buffer).Trim();

                // 安卓端广播 "ExtendScreenClient" — 自动发现
                if (data == "ExtendScreenClient")
                {
                    System.Diagnostics.Debug.WriteLine($"Received client broadcast from {result.RemoteEndPoint.Address}");
                    // 回复服务器信息
                    var response = Encoding.UTF8.GetBytes(_config.BroadcastMessage);
                    var clientEP = new IPEndPoint(result.RemoteEndPoint.Address, _config.DiscoveryPort);
                    try
                    {
                        await _udpClient!.SendAsync(response, response.Length, clientEP);
                    }
                    catch { }
                    continue;
                }

                // 解析安卓端发来的设备信息：格式 "DEVICE_INFO|name|width|height"
                if (data.StartsWith("DEVICE_INFO|"))
                {
                    var parts = data.Split('|');
                    if (parts.Length >= 4)
                    {
                        var device = new DeviceInfo
                        {
                            DeviceId = Guid.NewGuid().ToString("N")[..8],
                            DeviceName = parts[1],
                            IpAddress = result.RemoteEndPoint.Address.ToString(),
                            ScreenWidth = int.Parse(parts[2]),
                            ScreenHeight = int.Parse(parts[3]),
                            IsConnected = false
                        };
                        DeviceConnected?.Invoke(device);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Listen error: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _udpClient?.Close();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _udpClient?.Dispose();
    }
}
