using System.Runtime.InteropServices;

namespace ExtendScreenServer.Services;

/// <summary>
/// 虚拟显示器管理服务 — 通过 Parsec VDD 驱动创建/移除虚拟显示器
/// </summary>
public class VirtualDisplayService : IDisposable
{
    // Parsec VDD GUID
    private static readonly Guid VddAdapterGuid = new("00b41627-04c4-429e-a26e-0265cf50c8fa");
    private static readonly Guid VddClassGuid = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private const string VDD_HARDWARE_ID = "Root\\Parsec\\VDA";

    // IOCTL 控制码
    private const uint VDD_IOCTL_ADD = 0x0022e004;
    private const uint VDD_IOCTL_REMOVE = 0x0022a008;
    private const uint VDD_IOCTL_UPDATE = 0x0022a00c;

    private static readonly IntPtr InvalidHandle = new(-1);

    private IntPtr _deviceHandle = InvalidHandle;
    private int _displayIndex = -1;
    private CancellationTokenSource? _keepAliveCts;
    private bool _isInstalled;

    /// <summary>虚拟显示器是否已创建</summary>
    public bool IsVirtualDisplayCreated => _displayIndex >= 0;

    /// <summary>虚拟显示器的屏幕区域</summary>
    public System.Drawing.Rectangle VirtualScreenBounds { get; private set; }

    /// <summary>虚拟显示器的设备名（如 \\.\DISPLAY2）</summary>
    public string? VirtualDeviceName { get; private set; }

    /// <summary>驱动是否已安装</summary>
    public bool IsDriverInstalled => _isInstalled;

    /// <summary>最后一次错误信息</summary>
    public string LastError { get; private set; } = "";

    public VirtualDisplayService()
    {
        _isInstalled = CheckDriverInstalled();
    }

    private static bool IsInvalidHandle(IntPtr h) => h == InvalidHandle || h == IntPtr.Zero;

    /// <summary>
    /// 检查 Parsec VDD 驱动是否已安装
    /// </summary>
    private bool CheckDriverInstalled()
    {
        try
        {
            var classGuid = VddClassGuid;
            var devInfo = SetupDiGetClassDevsA(ref classGuid, null, IntPtr.Zero, DIGCF_PRESENT);
            if (IsInvalidHandle(devInfo))
            {
                LastError = "SetupDiGetClassDevsA 返回无效句柄";
                return false;
            }

            try
            {
                var devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>();

                uint deviceIndex = 0;
                bool foundHardware = false;
                while (SetupDiEnumDeviceInfo(devInfo, deviceIndex++, ref devInfoData))
                {
                    uint requiredSize = 0;
                    SetupDiGetDeviceRegistryPropertyA(devInfo, ref devInfoData,
                        SPDRP_HARDWAREID, IntPtr.Zero, null, 0, ref requiredSize);

                    if (requiredSize > 0)
                    {
                        var buffer = new byte[requiredSize];
                        if (SetupDiGetDeviceRegistryPropertyA(devInfo, ref devInfoData,
                            SPDRP_HARDWAREID, IntPtr.Zero, buffer, requiredSize, ref requiredSize))
                        {
                            var ids = System.Text.Encoding.ASCII.GetString(buffer)
                                .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

                            if (ids.Any(id => id.Equals(VDD_HARDWARE_ID, StringComparison.OrdinalIgnoreCase)))
                            {
                                foundHardware = true;
                                uint devStatus, problemNum;
                                uint cmResult = CM_Get_DevNode_Status(out devStatus, out problemNum, devInfoData.DevInst, 0);
                                if (cmResult != 0)
                                {
                                    LastError = $"找到VDD硬件但CM_Get_DevNode_Status失败(0x{cmResult:X})";
                                    return false;
                                }
                                if ((devStatus & DN_HAS_PROBLEM) != 0)
                                {
                                    LastError = $"VDD驱动存在问题(ProblemCode={problemNum})，可能需要重启或重新安装";
                                    return false;
                                }
                                if ((devStatus & (DN_DRIVER_LOADED | DN_STARTED)) != 0)
                                    return true;
                                
                                LastError = $"VDD设备状态异常(Status=0x{devStatus:X})";
                                return false;
                            }
                        }
                    }
                }
                LastError = foundHardware ? "VDD硬件未找到匹配ID" : "未在系统中找到Parsec VDD驱动，请先安装";
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }
        }
        catch (Exception ex)
        {
            LastError = $"检查驱动异常: {ex.Message}";
        }
        return false;
    }

    /// <summary>
    /// 创建虚拟显示器
    /// </summary>
    public bool CreateVirtualDisplay()
    {
        if (!_isInstalled)
        {
            LastError = $"VDD驱动未安装: {LastError}";
            return false;
        }

        try
        {
            _deviceHandle = OpenDeviceHandle();
            if (IsInvalidHandle(_deviceHandle))
            {
                int err = Marshal.GetLastWin32Error();
                LastError = $"无法打开VDD设备句柄(Win32错误={err})，请确认以管理员身份运行";
                return false;
            }

            // 记录当前屏幕列表
            var beforeScreens = System.Windows.Forms.Screen.AllScreens.ToList();

            // 添加虚拟显示器
            _displayIndex = VddIoControl(VDD_IOCTL_ADD, null, 0);
            if (_displayIndex < 0)
            {
                int err = Marshal.GetLastWin32Error();
                LastError = $"IOCTL_ADD失败(返回={_displayIndex}, Win32错误={err})";
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"Virtual display added, index: {_displayIndex}");

            // 启动保活线程（必须每 <100ms 调用一次 UPDATE）
            _keepAliveCts = new CancellationTokenSource();
            _ = Task.Run(KeepAliveLoop, _keepAliveCts.Token);

            // 强制切换为扩展模式（Windows 默认可能是复制模式）
            ForceExtendMode();

            // 等待虚拟显示器出现在屏幕列表中
            var virtualScreen = WaitForVirtualScreen(beforeScreens, 5000);
            if (virtualScreen != null)
            {
                VirtualScreenBounds = virtualScreen.Bounds;
                System.Diagnostics.Debug.WriteLine($"Virtual display bounds: {VirtualScreenBounds}");
                return true;
            }

            // 超时未找到，回退：使用默认分辨率
            System.Diagnostics.Debug.WriteLine("Virtual display not found in screen list, using default bounds.");
            VirtualScreenBounds = new System.Drawing.Rectangle(0, 0, 1920, 1080);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CreateVirtualDisplay error: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// 强制将显示模式切换为扩展（而非复制）
    /// </summary>
    private void ForceExtendMode()
    {
        try
        {
            // 使用 SetDisplayConfig API 强制扩展模式
            uint flags = SDC_APPLY | SDC_TOPOLOGY_EXTEND | SDC_USE_SUPPLIED_DISPLAY_CONFIG;
            long result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, flags);
            if (result != 0)
            {
                // 如果带 SDC_USE_SUPPLIED_DISPLAY_CONFIG 失败，尝试不带此标志
                flags = SDC_APPLY | SDC_TOPOLOGY_EXTEND;
                result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, flags);
            }
            System.Diagnostics.Debug.WriteLine($"ForceExtendMode: SetDisplayConfig result={result}");

            // 等待系统应用更改
            Thread.Sleep(500);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ForceExtendMode error: {ex.Message}");
        }
    }

    /// <summary>
    /// 等待虚拟显示器出现在屏幕列表中
    /// 使用 EnumDisplaySettingsEx 获取真实物理分辨率（不受 DPI 缩放影响）
    /// </summary>
    private System.Windows.Forms.Screen? WaitForVirtualScreen(List<System.Windows.Forms.Screen> beforeScreens, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(200);
            var currentScreens = System.Windows.Forms.Screen.AllScreens;

            foreach (var screen in currentScreens)
            {
                if (screen.Primary) continue;
                bool isNew = !beforeScreens.Any(b =>
                    b.Bounds == screen.Bounds &&
                    b.DeviceName == screen.DeviceName);

                if (isNew)
                {
                    // 保存设备名
                    VirtualDeviceName = screen.DeviceName;

                    // 使用 EnumDisplaySettingsEx 获取真实物理分辨率
                    var devMode = new DEVMODE();
                    devMode.dmSize = (short)Marshal.SizeOf<DEVMODE>();

                    if (EnumDisplaySettingsExA(screen.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode, 0))
                    {
                        int physWidth = (int)devMode.dmPelsWidth;
                        int physHeight = (int)devMode.dmPelsHeight;
                        int physX = devMode.dmPositionX;
                        int physY = devMode.dmPositionY;

                        VirtualScreenBounds = new System.Drawing.Rectangle(physX, physY, physWidth, physHeight);
                        System.Diagnostics.Debug.WriteLine(
                            $"Virtual display (EnumDisplaySettings): {screen.DeviceName} " +
                            $"Logical={screen.Bounds} Physical=({physX},{physY},{physWidth},{physHeight}) " +
                            $"Position=({devMode.dmPositionX},{devMode.dmPositionY})");
                    }
                    else
                    {
                        VirtualScreenBounds = screen.Bounds;
                        System.Diagnostics.Debug.WriteLine(
                            $"EnumDisplaySettingsEx failed for {screen.DeviceName}, using logical bounds");
                    }

                    return screen;
                }
            }
        }
        return null;
    }

    // EnumDisplaySettingsEx 相关
    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettingsExA(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    private async Task KeepAliveLoop()
    {
        while (!_keepAliveCts!.IsCancellationRequested)
        {
            try
            {
                VddIoControl(VDD_IOCTL_UPDATE, null, 0);
                await Task.Delay(80, _keepAliveCts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    /// <summary>
    /// 移除虚拟显示器
    /// </summary>
    public void RemoveVirtualDisplay()
    {
        if (_displayIndex >= 0 && !IsInvalidHandle(_deviceHandle))
        {
            try
            {
                ushort indexData = (ushort)(((_displayIndex & 0xFF) << 8) | ((_displayIndex >> 8) & 0xFF));
                VddIoControl(VDD_IOCTL_REMOVE, BitConverter.GetBytes(indexData), 2);
            }
            catch { }
            _displayIndex = -1;
        }
    }

    private IntPtr OpenDeviceHandle()
    {
        var adapterGuid = VddAdapterGuid;
        var devInfo = SetupDiGetClassDevsA(ref adapterGuid, null, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

        if (IsInvalidHandle(devInfo))
        {
            LastError = $"SetupDiGetClassDevsA失败(Win32={Marshal.GetLastWin32Error()})";
            return InvalidHandle;
        }

        try
        {
            var devInterface = new SP_DEVICE_INTERFACE_DATA();
            devInterface.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            int interfaceCount = 0;
            for (uint i = 0; SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref adapterGuid, i, ref devInterface); i++)
            {
                interfaceCount++;
                uint detailSize = 0;
                // 第一次调用获取需要的缓冲区大小
                SetupDiGetDeviceInterfaceDetailA(devInfo, ref devInterface, IntPtr.Zero, 0, ref detailSize, IntPtr.Zero);
                int detailErr = Marshal.GetLastWin32Error();
                if (detailSize == 0)
                {
                    LastError = $"SetupDiGetDeviceInterfaceDetailA获取大小失败(size=0, Win32={detailErr})";
                    continue;
                }

                var detailBuffer = Marshal.AllocHGlobal((int)detailSize);
                try
                {
                    // cbSize: 32位=5, 64位=8
                    int cbSize = IntPtr.Size == 8 ? 8 : 5;
                    Marshal.WriteInt32(detailBuffer, cbSize);

                    if (!SetupDiGetDeviceInterfaceDetailA(devInfo, ref devInterface, detailBuffer, detailSize, ref detailSize, IntPtr.Zero))
                    {
                        LastError = $"SetupDiGetDeviceInterfaceDetailA失败(Win32={Marshal.GetLastWin32Error()})";
                        continue;
                    }

                    // DevicePath 在 cbSize 之后开始，偏移量 = 4（DWORD的大小）
                    var devicePath = Marshal.PtrToStringAnsi(detailBuffer + 4);

                    if (string.IsNullOrEmpty(devicePath))
                    {
                        LastError = "获取到的设备路径为空";
                        continue;
                    }

                    LastError = $"尝试打开设备: {devicePath}";
                    System.Diagnostics.Debug.WriteLine($"VDD device path: {devicePath}");

                    var handle = CreateFileA(devicePath,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero, OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_NO_BUFFERING | FILE_FLAG_OVERLAPPED | FILE_FLAG_WRITE_THROUGH,
                        IntPtr.Zero);

                    int createErr = Marshal.GetLastWin32Error();
                    if (IsInvalidHandle(handle))
                    {
                        LastError = $"CreateFileA失败(Win32={createErr}): {devicePath}";
                        continue;
                    }

                    LastError = "";
                    return handle;
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }

            if (interfaceCount == 0)
            {
                LastError = "未找到VDD设备接口，驱动可能未正确安装或未启动";
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfo);
        }
        return InvalidHandle;
    }

    private int VddIoControl(uint ioctlCode, byte[]? data, int dataSize)
    {
        if (IsInvalidHandle(_deviceHandle))
            return -1;

        var inBuffer = new byte[32];
        if (data != null && dataSize > 0)
            Array.Copy(data, inBuffer, Math.Min(dataSize, 32));

        var overlapped = new NativeOverlapped();
        overlapped.EventHandle = CreateEventA(IntPtr.Zero, true, false, null);

        uint outBuffer = 0;
        bool result = DeviceIoControl(_deviceHandle, ioctlCode,
            inBuffer, (uint)inBuffer.Length,
            out outBuffer, sizeof(uint),
            out _, ref overlapped);

        if (!result)
        {
            uint bytesReturned = 0;
            if (!GetOverlappedResultEx(_deviceHandle, ref overlapped, out bytesReturned, 5000, false))
            {
                if (overlapped.EventHandle != IntPtr.Zero)
                    CloseHandle(overlapped.EventHandle);
                return -1;
            }
        }

        if (overlapped.EventHandle != IntPtr.Zero)
            CloseHandle(overlapped.EventHandle);

        return (int)outBuffer;
    }

    public void Dispose()
    {
        _keepAliveCts?.Cancel();
        RemoveVirtualDisplay();

        if (!IsInvalidHandle(_deviceHandle))
        {
            CloseHandle(_deviceHandle);
            _deviceHandle = InvalidHandle;
        }

        _keepAliveCts?.Dispose();
    }

    #region Win32 P/Invoke

    private const uint DIGCF_PRESENT = 0x02;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint SPDRP_HARDWAREID = 0x01;
    private const uint DN_DRIVER_LOADED = 0x00000004;
    private const uint DN_STARTED = 0x00000008;
    private const uint DN_HAS_PROBLEM = 0x00000400;

    // SetDisplayConfig 标志
    private const uint SDC_APPLY = 0x00000080;
    private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsA(ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyA(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property, IntPtr PropertyRegDataType, byte[]? PropertyBuffer, uint PropertyBufferSize, ref uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailA(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("cfgmgr32.dll", SetLastError = true)]
    private static extern uint CM_Get_DevNode_Status(out uint dnDevStatus, out uint dnProblemNumber, uint dnDevInst, uint ulFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr CreateFileA(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, out uint lpOutBuffer, int nOutBufferSize, out uint lpBytesReturned, ref NativeOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResultEx(IntPtr hFile, ref NativeOverlapped lpOverlapped, out uint lpNumberOfBytesTransferred, uint dwMilliseconds, bool bAlertable);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr CreateEventA(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern long SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray, uint numModeArrayElements, IntPtr modeArray, uint flags);

    #endregion
}
