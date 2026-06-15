using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ExtendScreenServer.Models;

namespace ExtendScreenServer.Services;

/// <summary>
/// 视频串流服务 — 使用 GDI 截图 + JPEG 编码，通过 TCP 发送
/// </summary>
public class StreamingService : IDisposable
{
    private readonly ServerConfig _config;
    private TcpListener? _videoListener;
    private TcpClient? _videoClient;
    private NetworkStream? _videoStream;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private bool _clientConnected;
    private bool _acceptNewClients = true; // 控制是否接受新客户端连接

    public event Action<bool>? ClientConnectionChanged;
    public bool IsConnected => _clientConnected;

    public StreamingService(ServerConfig config)
    {
        _config = config;
    }

    private int _captureX, _captureY; // 截图起始坐标（支持虚拟显示器偏移）
    private string? _monitorDeviceName; // 虚拟显示器设备名（用于 CreateDC）

    // 鼠标光标相关 P/Invoke
    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hCursor;
        public Point ptScreenPos;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    private const uint CURSOR_SHOWING = 0x00000001;
    private const uint DI_NORMAL = 0x0003;

    // BitBlt 相关 P/Invoke — 直接操作物理像素，不受 DPI 缩放影响
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr CreateDCA(string? lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    // DPI 感知上下文切换 — 让 GDI 操作使用物理像素坐标
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

    private const uint SRCCOPY = 0x00CC0020;

    public void Start(int width, int height)
    {
        Start(width, height, 0, 0);
    }

    public void Start(int width, int height, int captureX, int captureY, string? monitorDeviceName = null)
    {
        _config.ScreenWidth = width;
        _config.ScreenHeight = height;
        _captureX = captureX;
        _captureY = captureY;
        _monitorDeviceName = monitorDeviceName;

        _isRunning = true;
        _cts = new CancellationTokenSource();

        // 启动视频 TCP 监听
        _videoListener = new TcpListener(IPAddress.Any, _config.VideoStreamPort);
        _videoListener.Start();

        _ = Task.Run(AcceptClientLoop, _cts.Token);
    }

    private async Task AcceptClientLoop()
    {
        while (!_cts!.IsCancellationRequested)
        {
            try
            {
                // 等待允许新连接
                while (!_acceptNewClients && !_cts.IsCancellationRequested)
                {
                    await Task.Delay(200, _cts.Token);
                }
                if (_cts.IsCancellationRequested) break;

                _videoClient = await _videoListener!.AcceptTcpClientAsync(_cts.Token);
                _videoClient.NoDelay = true;
                _videoClient.SendBufferSize = 512 * 1024;
                _videoClient.ReceiveBufferSize = 8192;
                _videoStream = _videoClient.GetStream();
                _clientConnected = true;
                ClientConnectionChanged?.Invoke(true);

                // 开始发送视频帧循环
                await StreamingLoop(_cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Accept error: {ex.Message}");
            }

            _clientConnected = false;
            ClientConnectionChanged?.Invoke(false);
            _videoStream?.Close();
            _videoClient?.Close();
        }
    }

    private async Task StreamingLoop(CancellationToken token)
    {
        var fps = _config.TargetFps;
        var intervalMs = 1000 / fps;

        var jpegQuality = 25L;
        var frameCount = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 预创建 JPEG 编码器
        var jpegCodec = GetEncoder(ImageFormat.Jpeg);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);

        try
        {
            while (!token.IsCancellationRequested && _videoClient?.Connected == true)
            {
                var frameStart = sw.ElapsedMilliseconds;

                try
                {
                    // 切换 DPI 感知上下文，确保 BitBlt 使用物理像素
                    var prevDpiContext = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

                    IntPtr hdcScreen;
                    bool useMonitorDC = !string.IsNullOrEmpty(_monitorDeviceName);

                    if (useMonitorDC)
                    {
                        hdcScreen = CreateDCA("DISPLAY", _monitorDeviceName, null, IntPtr.Zero);
                    }
                    else
                    {
                        hdcScreen = GetDC(IntPtr.Zero);
                    }

                    IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
                    IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, _config.ScreenWidth, _config.ScreenHeight);
                    IntPtr hOld = SelectObject(hdcMem, hBitmap);

                    // BitBlt 截图
                    int srcX = useMonitorDC ? 0 : _captureX;
                    int srcY = useMonitorDC ? 0 : _captureY;
                    BitBlt(hdcMem, 0, 0, _config.ScreenWidth, _config.ScreenHeight,
                        hdcScreen, srcX, srcY, SRCCOPY);

                    // 绘制鼠标光标（自定义箭头，带黑色边框确保可见）
                    var ci = new CURSORINFO();
                    ci.cbSize = Marshal.SizeOf<CURSORINFO>();
                    if (GetCursorInfo(ref ci) && (ci.flags & CURSOR_SHOWING) != 0)
                    {
                        int cursorX = ci.ptScreenPos.X - _captureX;
                        int cursorY = ci.ptScreenPos.Y - _captureY;

                        if (cursorX >= 0 && cursorX < _config.ScreenWidth &&
                            cursorY >= 0 && cursorY < _config.ScreenHeight)
                        {
                            // 先尝试用系统光标绘制
                            DrawIconEx(hdcMem, cursorX, cursorY, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                            // 再画一个自定义箭头轮廓，确保在浅色背景上也能看到
                            DrawCursorArrow(hdcMem, cursorX, cursorY);
                        }
                    }

                    // 将 HBITMAP 转为 Bitmap 并 JPEG 编码
                    var capturedBitmap = Image.FromHbitmap(hBitmap);
                    using var ms = new MemoryStream();
                    capturedBitmap.Save(ms, jpegCodec, encoderParams);
                    capturedBitmap.Dispose();

                    var jpegData = ms.ToArray();
                    var jpegLength = jpegData.Length;

                    // 发送 4 字节长度头（大端序）
                    var header = new byte[4];
                    header[0] = (byte)(jpegLength >> 24);
                    header[1] = (byte)(jpegLength >> 16);
                    header[2] = (byte)(jpegLength >> 8);
                    header[3] = (byte)jpegLength;

                    if (_videoStream != null)
                    {
                        await _videoStream.WriteAsync(header, 0, 4, token);
                        await _videoStream.WriteAsync(jpegData, 0, jpegLength, token);

                        frameCount++;
                    }

                    // 清理 GDI 资源
                    SelectObject(hdcMem, hOld);
                    DeleteObject(hBitmap);
                    DeleteDC(hdcMem);
                    if (useMonitorDC)
                        DeleteDC(hdcScreen);
                    else
                        ReleaseDC(IntPtr.Zero, hdcScreen);

                    if (prevDpiContext != IntPtr.Zero)
                        SetThreadDpiAwarenessContext(prevDpiContext);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Stream error: {ex.Message}");
                    break;
                }

                // 帧率控制
                var elapsed = (int)(sw.ElapsedMilliseconds - frameStart);
                var delay = intervalMs - elapsed;
                if (delay > 0)
                {
                    try { await Task.Delay(delay, token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine($"Streaming stopped. Total frames: {frameCount}");
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr lppt);

    [DllImport("gdi32.dll")]
    private static extern bool LineTo(IntPtr hdc, int x, int y);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

    private static void DrawCursorArrow(IntPtr hdc, int x, int y)
    {
        // 标准箭头光标的轮廓点（基于Windows默认光标形状）
        var arrowPoints = new[]
        {
            new { X = 0, Y = 0 },   // 箭头尖端
            new { X = 0, Y = 21 },
            new { X = 5, Y = 16 },
            new { X = 9, Y = 25 },
            new { X = 12, Y = 24 },
            new { X = 8, Y = 15 },
            new { X = 14, Y = 15 },
            new { X = 0, Y = 0 },
        };

        // 黑色边框（稍大）
        var borderPen = CreatePen(0, 2, 0x00000000); // 黑色
        var oldPen = SelectObject(hdc, borderPen);
        var oldBrush = SelectObject(hdc, GetStockObject(NULL_BRUSH));
        for (int i = 0; i < arrowPoints.Length - 1; i++)
        {
            MoveToEx(hdc, x + arrowPoints[i].X, y + arrowPoints[i].Y, IntPtr.Zero);
            LineTo(hdc, x + arrowPoints[i + 1].X, y + arrowPoints[i + 1].Y);
        }
        SelectObject(hdc, oldBrush);
        SelectObject(hdc, oldPen);
        DeleteObject(borderPen);

        // 白色填充
        var whitePen = CreatePen(0, 1, 0x00FFFFFF); // 白色
        var whiteBrush = CreateSolidBrush(0x00FFFFFF);
        oldPen = SelectObject(hdc, whitePen);
        oldBrush = SelectObject(hdc, whiteBrush);
        // 用多边形填充
        var pts = new POINT[arrowPoints.Length - 1];
        for (int i = 0; i < pts.Length; i++)
        {
            pts[i] = new POINT { X = x + arrowPoints[i].X, Y = y + arrowPoints[i].Y };
        }
        Polygon(hdc, pts, pts.Length);
        SelectObject(hdc, oldBrush);
        SelectObject(hdc, oldPen);
        DeleteObject(whitePen);
        DeleteObject(whiteBrush);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern bool Polygon(IntPtr hdc, POINT[] lpPoints, int nCount);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    private const int NULL_BRUSH = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        return codecs[0];
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _videoStream?.Close();
        _videoClient?.Close();
        _videoListener?.Stop();
        _clientConnected = false;
    }

    /// <summary>
    /// 只断开当前客户端，不再接受新连接（手动断开）
    /// </summary>
    public void DisconnectClient()
    {
        _acceptNewClients = false;
        _videoStream?.Close();
        _videoClient?.Close();
        _videoStream = null;
        _videoClient = null;
        _clientConnected = false;
        ClientConnectionChanged?.Invoke(false);
    }

    /// <summary>
    /// 重新允许接受新客户端连接
    /// </summary>
    public void AllowNewConnections()
    {
        _acceptNewClients = true;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
