package com.extendscreen

data class ServerInfo(
    val ipAddress: String,
    val videoPort: Int = 12348,
    val inputPort: Int = 12347,
    val hostname: String = "Unknown"
)

data class DeviceConfig(
    val screenWidth: Int = 1080,
    val screenHeight: Int = 1920,
    val targetFps: Int = 30,
    val bitrate: Int = 8_000_000
)

enum class ConnectionState {
    DISCONNECTED,
    CONNECTING,
    CONNECTED,
    ERROR
}
