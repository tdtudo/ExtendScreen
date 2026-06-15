using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using ExtendScreenServer.Models;
using ExtendScreenServer.Services;

namespace ExtendScreenServer.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ServerConfig _config;
    private readonly DiscoveryService _discoveryService;
    private readonly StreamingService _streamingService;
    private readonly NetworkServerService _networkService;
    private readonly VirtualDisplayService _virtualDisplayService;

    public event PropertyChangedEventHandler? PropertyChanged;

    private DeviceInfo? _connectedDevice;
    public DeviceInfo? ConnectedDevice
    {
        get => _connectedDevice;
        set { _connectedDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsConnected)); }
    }

    public bool IsConnected => ConnectedDevice != null;

    private string _statusText = "未连接";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private int _selectedResolutionIndex = 0;
    public int SelectedResolutionIndex
    {
        get => _selectedResolutionIndex;
        set { _selectedResolutionIndex = value; OnPropertyChanged(); ApplyResolution(); }
    }

    private int _selectedFpsIndex = 0;
    public int SelectedFpsIndex
    {
        get => _selectedFpsIndex;
        set { _selectedFpsIndex = value; OnPropertyChanged(); ApplyFps(); }
    }

    private string _connectionType = "Wi-Fi";
    public string ConnectionType
    {
        get => _connectionType;
        set { _connectionType = value; OnPropertyChanged(); }
    }

    private bool _isUsbForwarding;
    public bool IsUsbForwarding
    {
        get => _isUsbForwarding;
        set { _isUsbForwarding = value; OnPropertyChanged(); }
    }

    private bool _isExtending;
    public bool IsExtending
    {
        get => _isExtending;
        set { _isExtending = value; OnPropertyChanged(); }
    }

    private string _displayModeText = "投屏模式";
    public string DisplayModeText
    {
        get => _displayModeText;
        set { _displayModeText = value; OnPropertyChanged(); }
    }

    // 模式相关属性
    private bool _isWifiMode = true;
    public bool IsWifiMode
    {
        get => _isWifiMode;
        set { _isWifiMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModeLabelText)); OnPropertyChanged(nameof(ModeDescription)); }
    }

    public string ModeLabelText => IsWifiMode ? "Wi-Fi 模式" : "USB 模式";
    public string ModeDescription => IsWifiMode
        ? "手机和电脑在同一局域网，手机端自动发现电脑并连接"
        : "通过 ADB 端口转发连接，支持 USB 线或无线调试";

    private bool _isServiceStarted;
    public bool IsServiceStarted
    {
        get => _isServiceStarted;
        set { _isServiceStarted = value; OnPropertyChanged(); }
    }

    public string[] ResolutionOptions { get; } = { "自适应", "1280x720", "1920x1080", "2560x1440" };
    public string[] FpsOptions { get; } = { "30 fps", "60 fps" };

    private string _localIpAddress = "获取中...";
    public string LocalIpAddress
    {
        get => _localIpAddress;
        set { _localIpAddress = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        _config = new ServerConfig();
        _discoveryService = new DiscoveryService(_config);
        _streamingService = new StreamingService(_config);
        _networkService = new NetworkServerService(_config);
        _virtualDisplayService = new VirtualDisplayService();

        _discoveryService.DeviceConnected += OnDeviceFound;
        _networkService.InputEventReceived += OnInputReceived;

        LocalIpAddress = GetLocalIP();

        if (!_virtualDisplayService.IsDriverInstalled)
        {
            DisplayModeText = "投屏模式（未安装虚拟显示驱动）";
        }
    }

    private static string GetLocalIP()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var name = iface.Name.ToLower();
                if (name.Contains("virtual") || name.Contains("vmware") ||
                    name.Contains("virtualbox") || name.Contains("hyper-v") ||
                    name.Contains("pseudo") || name.Contains("bluetooth") ||
                    name.Contains("wsl") || name.Contains("docker"))
                    continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ip = addr.Address.ToString();
                        if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                            return ip;
                    }
                }
            }

            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                        return addr.Address.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    /// <summary>
    /// 选择 Wi-Fi 模式并启动服务
    /// </summary>
    public async Task StartWifiModeAsync()
    {
        IsWifiMode = true;
        ConnectionType = "Wi-Fi";
        await StartServicesAsync();
    }

    /// <summary>
    /// 选择 USB 模式并启动服务
    /// </summary>
    public async Task StartUsbModeAsync()
    {
        IsWifiMode = false;
        ConnectionType = "USB";
        await StartServicesAsync();

        // USB 模式自动设置端口转发（在后台线程执行，避免卡死 UI）
        await SetupUsbForwardAsync();
    }

    public async Task StartServicesAsync()
    {
        if (_isServiceStarted) return;

        try
        {
            StatusText = "正在启动服务...";

            int captureWidth, captureHeight, captureX, captureY;

            if (_virtualDisplayService.IsDriverInstalled)
            {
                StatusText = "正在创建虚拟显示器...";
                bool created = await Task.Run(() => _virtualDisplayService.CreateVirtualDisplay());

                if (created && _virtualDisplayService.IsVirtualDisplayCreated)
                {
                    var allScreens = System.Windows.Forms.Screen.AllScreens;
                    System.Diagnostics.Debug.WriteLine($"=== 所有屏幕 ({allScreens.Length}) ===");
                    foreach (var s in allScreens)
                    {
                        System.Diagnostics.Debug.WriteLine($"  {s.DeviceName}: {s.Bounds} Primary={s.Primary}");
                    }

                    var bounds = _virtualDisplayService.VirtualScreenBounds;
                    captureWidth = bounds.Width;
                    captureHeight = bounds.Height;
                    captureX = bounds.X;
                    captureY = bounds.Y;

                    _config.ScreenWidth = captureWidth;
                    _config.ScreenHeight = captureHeight;

                    InputInjector.SetScreenBounds(captureWidth, captureHeight);
                    InputInjector.SetScreenOffset(captureX, captureY);

                    IsExtending = true;
                    DisplayModeText = $"扩展模式 {captureWidth}x{captureHeight} @({captureX},{captureY})";
                    StatusText = $"虚拟显示器已创建 {captureWidth}x{captureHeight} @({captureX},{captureY})";
                }
                else
                {
                    var screen = System.Windows.Forms.Screen.PrimaryScreen;
                    captureWidth = screen?.Bounds.Width ?? 1920;
                    captureHeight = screen?.Bounds.Height ?? 1080;
                    captureX = 0;
                    captureY = 0;

                    _config.ScreenWidth = captureWidth;
                    _config.ScreenHeight = captureHeight;
                    InputInjector.SetScreenBounds(captureWidth, captureHeight);

                    DisplayModeText = $"投屏模式（创建失败: {_virtualDisplayService.LastError}）";
                }
            }
            else
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                captureWidth = screen?.Bounds.Width ?? 1920;
                captureHeight = screen?.Bounds.Height ?? 1080;
                captureX = 0;
                captureY = 0;

                _config.ScreenWidth = captureWidth;
                _config.ScreenHeight = captureHeight;
                InputInjector.SetScreenBounds(captureWidth, captureHeight);

                DisplayModeText = $"投屏模式（{_virtualDisplayService.LastError}）";
            }

            _streamingService.Start(captureWidth, captureHeight, captureX, captureY, _virtualDisplayService.VirtualDeviceName);
            _streamingService.ClientConnectionChanged += connected =>
            {
                if (connected)
                {
                    var mode = IsExtending ? "扩展" : "投屏";
                    StatusText = $"已连接 | {mode}模式 | {_config.ScreenWidth}x{_config.ScreenHeight} @{_config.TargetFps}fps";
                }
                else
                {
                    StatusText = "视频连接已断开，等待重连...";
                    ConnectedDevice = null;
                }
            };

            // Wi-Fi 模式才启动 UDP 发现服务
            if (IsWifiMode)
            {
                _discoveryService.Start();
            }

            await _networkService.StartAsync();

            _isServiceStarted = true;
            IsServiceStarted = true;

            var modeText = IsExtending ? "扩展" : "投屏";
            var connMode = IsWifiMode ? "Wi-Fi" : "USB";
            StatusText = IsWifiMode
                ? $"等待设备连接... [{modeText}模式] IP: {LocalIpAddress}"
                : $"USB 转发已启动，请在手机端点击 USB 连接 [{modeText}模式]";
        }
        catch (Exception ex)
        {
            StatusText = $"启动失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 停止所有服务，返回模式选择
    /// </summary>
    public void StopServices()
    {
        Disconnect();
        StopUsbForward();

        _streamingService.Stop();
        _networkService.Stop();
        _discoveryService.Stop();

        _virtualDisplayService.RemoveVirtualDisplay();

        _isServiceStarted = false;
        IsServiceStarted = false;
        ConnectedDevice = null;
        StatusText = "未连接";
    }

    private async Task SetupUsbForwardAsync()
    {
        StatusText = "正在设置 ADB 转发...";

        try
        {
            var adbPath = await Task.Run(() => FindAdbPath());
            if (adbPath == null)
            {
                StatusText = "未找到 adb，请安装 Android SDK Platform Tools";
                return;
            }

            // 先启动 adb server
            await Task.Run(() => RunAdbCommand(adbPath, "start-server"));

            // 检查设备连接
            var devicesOutput = await Task.Run(() => RunAdbCommandWithOutput(adbPath, "devices"));

            if (string.IsNullOrEmpty(devicesOutput))
            {
                StatusText = "ADB 命令执行失败，请检查 ADB 是否正常";
                return;
            }

            // 检查是否有 offline 设备，尝试重连
            if (devicesOutput.Contains("\toffline"))
            {
                StatusText = "检测到设备离线，正在尝试重连...";
                await Task.Run(() =>
                {
                    RunAdbCommand(adbPath, "reconnect");
                    System.Threading.Thread.Sleep(2000);
                });

                devicesOutput = await Task.Run(() => RunAdbCommandWithOutput(adbPath, "devices"));

                if (!devicesOutput.Contains("\tdevice"))
                {
                    StatusText = "设备离线，请在手机上重新授权 USB 调试，或重新配对无线调试";
                    return;
                }
            }
            else if (!devicesOutput.Contains("\tdevice"))
            {
                StatusText = "未检测到已连接的设备，请通过 USB 线或无线调试连接设备";
                return;
            }

            var videoPort = _config.VideoStreamPort;
            var inputPort = _config.InputEventPort;
            var fwdVideoPort = _config.UsbForwardVideoPort;
            var fwdInputPort = _config.UsbForwardInputPort;

            await Task.Run(() =>
            {
                // 使用 adb reverse：设备本地端口 → PC 端口（设备主动连 PC）
                RunAdbCommand(adbPath, $"reverse tcp:{fwdVideoPort} tcp:{videoPort}");
                RunAdbCommand(adbPath, $"reverse tcp:{fwdInputPort} tcp:{inputPort}");
            });

            _streamingService.AllowNewConnections();
            IsUsbForwarding = true;

            var modeText = IsExtending ? "扩展" : "投屏";
            StatusText = $"ADB 转发已启动，请在手机端点击 USB 连接 [{modeText}模式]";
        }
        catch (Exception ex)
        {
            StatusText = $"ADB 转发失败: {ex.Message}";
        }
    }

    private void OnDeviceFound(DeviceInfo device)
    {
        if (ConnectedDevice == null)
        {
            _streamingService.AllowNewConnections();
            device.IsConnected = true;
            ConnectedDevice = device;
            ConnectionType = device.ConnectionType == Models.ConnectionType.USB ? "USB" : "Wi-Fi";
            StatusText = $"已连接: {device.DeviceName} | {_config.ScreenWidth}x{_config.ScreenHeight} @{_config.TargetFps}fps";
        }
    }

    private void OnInputReceived(InputEvent input)
    {
        _networkService.InjectInput(input);
    }

    private void ApplyResolution()
    {
        var resolutions = new[] { (0, 0), (1280, 720), (1920, 1080), (2560, 1440) };
        var (w, h) = resolutions[SelectedResolutionIndex];
        if (w > 0) _config.ScreenWidth = w;
        if (h > 0) _config.ScreenHeight = h;
    }

    private void ApplyFps()
    {
        _config.TargetFps = SelectedFpsIndex == 0 ? 30 : 60;
    }

    public void Disconnect()
    {
        _streamingService.DisconnectClient();
        ConnectedDevice = null;
        var modeText = IsExtending ? "扩展" : "投屏";
        var connMode = IsWifiMode ? "Wi-Fi" : "USB";
        StatusText = IsWifiMode
            ? $"已断开，等待重新连接... [{modeText}模式] IP: {LocalIpAddress}"
            : $"已断开，等待重新连接... [USB模式]";
    }

    public void StopUsbForward()
    {
        try
        {
            var adbPath = FindAdbPath();
            if (adbPath != null)
            {
                RunAdbCommand(adbPath, $"reverse --remove tcp:{_config.UsbForwardVideoPort}");
                RunAdbCommand(adbPath, $"reverse --remove tcp:{_config.UsbForwardInputPort}");
            }
        }
        catch { }
        IsUsbForwarding = false;
    }

    private static string? FindAdbPath()
    {
        var candidates = new[]
        {
            @"D:\professionalSoftware\Android\SDK\platform-tools\adb.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "Sdk", "platform-tools", "adb.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = "version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null) return "adb";
        }
        catch { }

        return null;
    }

    private static void RunAdbCommand(string adbPath, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000); // 5秒超时
        if (proc != null && !proc.HasExited)
            proc.Kill();
    }

    /// <summary>
    /// 执行 ADB 命令并返回输出（先读输出再等待退出，避免死锁）
    /// </summary>
    private static string RunAdbCommandWithOutput(string adbPath, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";

            // 先读取输出，再等待退出（避免死锁）
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(8000);
            if (!proc.HasExited)
            {
                proc.Kill();
                return output; // 返回已读取的部分输出
            }
            return output;
        }
        catch
        {
            return "";
        }
    }

    public void Shutdown()
    {
        StopServices();
        _virtualDisplayService.Dispose();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
