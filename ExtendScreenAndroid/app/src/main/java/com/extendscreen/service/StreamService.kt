package com.extendscreen.service

import android.app.*
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Binder
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.extendscreen.ConnectionState
import com.extendscreen.ServerInfo
import com.extendscreen.network.DiscoveryClient
import com.extendscreen.network.InputSender
import com.extendscreen.network.VideoStreamClient
import kotlinx.coroutines.*

class StreamService : Service() {

    companion object {
        private const val CHANNEL_ID = "extendscreen_stream"
        private const val NOTIFICATION_ID = 1
    }

    inner class StreamBinder : Binder() {
        fun getService(): StreamService = this@StreamService
    }

    private val binder = StreamBinder()
    private val scope = CoroutineScope(Dispatchers.Main + SupervisorJob())
    private val discoveryClient = DiscoveryClient()
    private val videoClient = VideoStreamClient()
    private val inputSender = InputSender()

    var connectionState = ConnectionState.DISCONNECTED
        private set
    var connectedServer: ServerInfo? = null
        private set
    private var manualDisconnect = false

    var onStateChanged: ((ConnectionState) -> Unit)? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForeground(NOTIFICATION_ID, createNotification("等待连接..."),
            ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        // 不再自动连接，等待用户选择模式
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder = binder

    /**
     * Wi-Fi 模式：开始搜索电脑
     */
    fun startWifiDiscovery() {
        manualDisconnect = false
        updateState(ConnectionState.CONNECTING)

        discoveryClient.onServerFound = { server ->
            if (connectionState != ConnectionState.CONNECTED && !videoClient.isConnecting) {
                connectedServer = server
                connectToServer(server)
            }
        }

        discoveryClient.startDiscovery(scope)
    }

    /**
     * 停止搜索
     */
    fun stopDiscovery() {
        discoveryClient.stop()
    }

    fun connectManual(ip: String) {
        manualDisconnect = false
        discoveryClient.stop()
        videoClient.disconnect()
        inputSender.disconnect()
        updateState(ConnectionState.CONNECTING)

        val server = ServerInfo(ipAddress = ip, hostname = ip)
        connectedServer = server
        connectToServer(server)
    }

    fun connectUsb() {
        manualDisconnect = false
        discoveryClient.stop()
        videoClient.disconnect()
        inputSender.disconnect()
        updateState(ConnectionState.CONNECTING)

        // USB/ADB 模式：通过 ADB forward 连接，使用转发端口（避免与电脑端监听端口冲突）
        val server = ServerInfo(ipAddress = "127.0.0.1", videoPort = 22348, inputPort = 22347, hostname = "USB")
        connectedServer = server
        connectToServer(server)
    }

    private fun connectToServer(server: ServerInfo) {
        videoClient.onConnected = {
            discoveryClient.stop()
            updateState(ConnectionState.CONNECTED)
            startForeground(NOTIFICATION_ID, createNotification("已连接"),
                ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
            inputSender.connect(server, scope)
        }

        videoClient.onDisconnected = {
            inputSender.disconnect()
            if (manualDisconnect) {
                updateState(ConnectionState.DISCONNECTED)
            } else {
                updateState(ConnectionState.DISCONNECTED)
                // 意外断开时，根据当前模式重连
                if (server.hostname == "USB") {
                    // USB 模式不自动重连
                } else {
                    startWifiDiscovery()
                }
            }
        }

        videoClient.connect(server, scope)
    }

    private fun updateState(state: ConnectionState) {
        connectionState = state
        onStateChanged?.invoke(state)
    }

    fun sendTouchEvent(type: String, x: Float, y: Float, action: Int, pointerCount: Int) {
        inputSender.sendTouchEvent(type, x, y, action, pointerCount)
    }

    fun sendScrollEvent(deltaX: Float, deltaY: Float) {
        inputSender.sendScrollEvent(deltaX, deltaY)
    }

    fun sendZoomEvent(scale: Float) {
        inputSender.sendZoomEvent(scale)
    }

    fun disconnectAndRestart() {
        manualDisconnect = true
        discoveryClient.stop()
        videoClient.onConnected = null
        videoClient.onDisconnected = null
        videoClient.disconnect()
        inputSender.disconnect()
        connectedServer = null
        updateState(ConnectionState.DISCONNECTED)
    }

    fun disconnect() {
        discoveryClient.stop()
        videoClient.disconnect()
        inputSender.disconnect()
        updateState(ConnectionState.DISCONNECTED)
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "ExtendScreen",
            NotificationManager.IMPORTANCE_LOW
        ).apply { description = "屏幕扩展服务" }
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(channel)
    }

    private fun createNotification(text: String): Notification {
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("ExtendScreen")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_menu_view)
            .setOngoing(true)
            .build()
    }

    override fun onDestroy() {
        disconnect()
        scope.cancel()
        super.onDestroy()
    }
}
