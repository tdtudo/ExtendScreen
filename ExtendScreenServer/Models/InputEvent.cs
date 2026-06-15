namespace ExtendScreenServer.Models;

/// <summary>
/// 触控输入事件
/// </summary>
public class InputEvent
{
    public InputEventType Type { get; set; }
    public float X { get; set; }   // 比例坐标 0.0~1.0
    public float Y { get; set; }
    public float DeltaX { get; set; }
    public float DeltaY { get; set; }
    public float Scale { get; set; }
    public int PointerCount { get; set; }
    public int Action { get; set; } // 0=down, 1=move, 2=up
    public long Timestamp { get; set; }

    public static InputEvent? Deserialize(ReadOnlySpan<byte> data)
    {
        try
        {
            var msg = System.Text.Encoding.UTF8.GetString(data);
            var parts = msg.Split('|');
            if (parts.Length < 8) return null;

            return new InputEvent
            {
                Type = Enum.TryParse<InputEventType>(parts[0], out var t) ? t : InputEventType.Touch,
                X = float.Parse(parts[1]),
                Y = float.Parse(parts[2]),
                DeltaX = float.Parse(parts[3]),
                DeltaY = float.Parse(parts[4]),
                Scale = float.Parse(parts[5]),
                PointerCount = int.Parse(parts[6]),
                Action = int.Parse(parts[7]),
                Timestamp = parts.Length > 8 ? long.Parse(parts[8]) : 0
            };
        }
        catch
        {
            return null;
        }
    }
}

public enum InputEventType
{
    Touch,
    Scroll,
    Zoom
}
