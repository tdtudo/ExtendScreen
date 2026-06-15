# ExtendScreen

English | [中文](README.md)

Use your Android device as an extended display for your Windows PC, supporting both USB and Wi-Fi connections.

## Features

- Extended Display: Use your tablet/phone as a second monitor for your PC
- USB Connection: Low latency via ADB port forwarding
- Wi-Fi Connection: Wireless extension, no cable needed
- PC-to-PC: Extend to another Windows PC as a secondary display
- Keep Screen On: Prevents Android device from sleeping while connected
- Mouse Sync: Normal cursor displayed on the extended screen

## Project Structure

```
ExtendScreenAndroid/    # Android app - receives and displays video stream
ExtendScreenServer/     # Windows server - captures screen and streams
ExtendScreenClient/     # Windows client - acts as an extended display on another PC
```

## How It Works

1. The Windows server captures the screen of a specified region, encodes it as JPEG frames, and sends them via TCP
2. The Android app receives JPEG frames, decodes them with hardware decoder, and renders to a SurfaceView
3. The Android app captures touch/input events and sends them back to the Windows server for mouse injection
4. Communication is established via ADB port forwarding (USB) or LAN TCP (Wi-Fi)

## Usage

### Prerequisites

- Windows 10/11 (x64)
- Android 8.0+ device
- ADB (Android Debug Bridge)
- USB debugging enabled (for USB connection)

### USB Connection

1. Connect your Android device to the PC with a USB cable
2. Enable USB debugging
3. Launch the Windows app and select USB mode
4. Tap Connect on the Android app

### Wi-Fi Connection

1. Ensure the PC and Android device are on the same LAN
2. Launch the Windows app and select Wi-Fi mode
3. Enter the PC's IP address on the Android app and connect

## Build

### Android

```bash
cd ExtendScreenAndroid
./gradlew assembleDebug
```

The APK will be at `app/build/outputs/apk/debug/`

### Windows

```bash
cd ExtendScreenServer
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o output
```

## Tech Stack

- **Android**: Kotlin, MediaCodec hardware decoding, SurfaceView rendering
- **Windows**: C# / WPF, .NET 7, GDI+ screen capture, TCP communication
- **Communication**: TCP Socket, ADB port forwarding, JPEG frame streaming

## License

MIT
