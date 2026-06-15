# ExtendScreen

[English](#english) | [中文](#中文)

---

## 中文

将 Android 设备作为 Windows 电脑的扩展屏幕，支持 USB 和 Wi-Fi 连接。

### 功能

- 扩展屏幕：将平板/手机作为电脑的第二显示器
- USB 连接：通过 ADB 端口转发，低延迟
- Wi-Fi 连接：无线扩展，无需数据线
- 电脑扩展电脑：支持将另一台 Windows 电脑作为扩展屏幕
- 防止息屏：连接时自动保持 Android 设备屏幕常亮
- 鼠标同步：扩展屏幕上显示正常鼠标光标

### 项目结构

```
ExtendScreenAndroid/    # Android 端 - 接收视频流并显示
ExtendScreenServer/     # Windows 服务端 - 捕获屏幕并传输
ExtendScreenClient/     # Windows 客户端 - 作为扩展屏幕的 Windows 端
```

### 原理

1. Windows 端捕获指定区域的屏幕画面，编码为 JPEG 帧通过 TCP 发送
2. Android 端接收 JPEG 帧并解码渲染到 SurfaceView
3. Android 端捕获触摸/输入事件，发送回 Windows 端注入鼠标操作
4. 通过 ADB 端口转发（USB）或局域网 TCP（Wi-Fi）建立通信

### 使用方法

#### 前置要求

- Windows 10/11（x64）
- Android 8.0+ 设备
- ADB（Android Debug Bridge）
- USB 连接需开启 USB 调试

#### USB 连接

1. 用数据线连接 Android 设备和电脑
2. 开启 USB 调试
3. 启动 Windows 端，选择 USB 模式
4. 在 Android 端点击连接

#### Wi-Fi 连接

1. 确保电脑和 Android 设备在同一局域网
2. 启动 Windows 端，选择 Wi-Fi 模式
3. 在 Android 端输入电脑 IP 地址并连接

### 构建

#### Android 端

```bash
cd ExtendScreenAndroid
./gradlew assembleDebug
```

输出 APK 位于 `app/build/outputs/apk/debug/`

#### Windows 端

```bash
cd ExtendScreenServer
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o output
```

### 技术栈

- **Android**：Kotlin, MediaCodec 硬件解码, SurfaceView 渲染
- **Windows**：C# / WPF, .NET 7, GDI+ 屏幕捕获, TCP 通信
- **通信**：TCP Socket, ADB 端口转发, JPEG 帧传输

---

## English

Use your Android device as an extended display for your Windows PC, supporting both USB and Wi-Fi connections.

### Features

- Extended Display: Use your tablet/phone as a second monitor for your PC
- USB Connection: Low latency via ADB port forwarding
- Wi-Fi Connection: Wireless extension, no cable needed
- PC-to-PC: Extend to another Windows PC as a secondary display
- Keep Screen On: Prevents Android device from sleeping while connected
- Mouse Sync: Normal cursor displayed on the extended screen

### Project Structure

```
ExtendScreenAndroid/    # Android app - receives and displays video stream
ExtendScreenServer/     # Windows server - captures screen and streams
ExtendScreenClient/     # Windows client - acts as an extended display on another PC
```

### How It Works

1. The Windows server captures the screen of a specified region, encodes it as JPEG frames, and sends them via TCP
2. The Android app receives JPEG frames, decodes them with hardware decoder, and renders to a SurfaceView
3. The Android app captures touch/input events and sends them back to the Windows server for mouse injection
4. Communication is established via ADB port forwarding (USB) or LAN TCP (Wi-Fi)

### Usage

#### Prerequisites

- Windows 10/11 (x64)
- Android 8.0+ device
- ADB (Android Debug Bridge)
- USB debugging enabled (for USB connection)

#### USB Connection

1. Connect your Android device to the PC with a USB cable
2. Enable USB debugging
3. Launch the Windows app and select USB mode
4. Tap Connect on the Android app

#### Wi-Fi Connection

1. Ensure the PC and Android device are on the same LAN
2. Launch the Windows app and select Wi-Fi mode
3. Enter the PC's IP address on the Android app and connect

### Build

#### Android

```bash
cd ExtendScreenAndroid
./gradlew assembleDebug
```

The APK will be at `app/build/outputs/apk/debug/`

#### Windows

```bash
cd ExtendScreenServer
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o output
```

### Tech Stack

- **Android**: Kotlin, MediaCodec hardware decoding, SurfaceView rendering
- **Windows**: C# / WPF, .NET 7, GDI+ screen capture, TCP communication
- **Communication**: TCP Socket, ADB port forwarding, JPEG frame streaming

## License

MIT
