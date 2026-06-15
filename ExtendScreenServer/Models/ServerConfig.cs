namespace ExtendScreenServer.Models;

/// <summary>
/// 服务端配置
/// </summary>
public class ServerConfig
{
    public int DiscoveryPort { get; set; } = 12345;
    public int VideoStreamPort { get; set; } = 12348;
    public int InputEventPort { get; set; } = 12347;
    // USB/ADB 转发使用的本地端口（避免与监听端口冲突）
    public int UsbForwardVideoPort { get; set; } = 22348;
    public int UsbForwardInputPort { get; set; } = 22347;
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public int TargetFps { get; set; } = 30;
    public int VideoBitrate { get; set; } = 8_000_000; // 8 Mbps
    public string BroadcastMessage { get; set; } = "ExtendScreenServer";
    public int BroadcastIntervalMs { get; set; } = 2000;
}
