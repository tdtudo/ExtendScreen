namespace ExtendScreenServer.Models;

/// <summary>
/// 连接的安卓设备信息
/// </summary>
public class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int VideoPort { get; set; }
    public int InputPort { get; set; }
    public ConnectionType ConnectionType { get; set; } = ConnectionType.WiFi;
    public bool IsConnected { get; set; }
    public DateTime ConnectedTime { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
}

public enum ConnectionType
{
    WiFi,
    USB
}
