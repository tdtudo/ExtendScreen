package com.extendscreen.network

import com.extendscreen.ServerInfo
import kotlinx.coroutines.*
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress

/**
 * UDP 客户端：监听 Windows 服务端的广播，自动发现 ExtendScreen Server
 *
 * 流程：
 * 1. Windows 端广播 "ExtendScreenServer" 到 255.255.255.255:12345
 * 2. Android 端绑定端口 12345 接收广播
 * 3. 同时 Android 端也广播 "ExtendScreenClient" 到 12346，Windows 端监听
 * 4. 收到广播后，向服务器 12346 端口回传设备信息
 */
class DiscoveryClient {

    companion object {
        private const val DISCOVERY_PORT = 12345
        private const val RESPONSE_PORT = 12346   // 回传端口 = DISCOVERY_PORT + 1
        private const val BROADCAST_MESSAGE = "ExtendScreenServer"
        private const val CLIENT_BROADCAST = "ExtendScreenClient"
        private const val TAG = "DiscoveryClient"
    }

    private var socket: DatagramSocket? = null
    private var isScanning = false
    private var job: Job? = null

    var onServerFound: ((ServerInfo) -> Unit)? = null

    fun startDiscovery(scope: CoroutineScope) {
        isScanning = true
        job = scope.launch(Dispatchers.IO) {
            try {
                // 绑定到 12345 端口，接收 Windows 的 UDP 广播
                val addr = InetSocketAddress(DISCOVERY_PORT)
                socket = DatagramSocket(null)
                socket!!.reuseAddress = true
                socket!!.broadcast = true
                socket!!.soTimeout = 2000
                socket!!.bind(addr)

                android.util.Log.d(TAG, "Discovery listening on port $DISCOVERY_PORT")

                val buffer = ByteArray(1024)
                while (isScanning && socket != null) {
                    // 同时发送客户端广播，让 Windows 端也能发现我们
                    try {
                        val clientBroadcast = CLIENT_BROADCAST.toByteArray()
                        val broadcastAddr = InetAddress.getByName("255.255.255.255")
                        val broadcastPacket = DatagramPacket(clientBroadcast, clientBroadcast.size, broadcastAddr, RESPONSE_PORT)
                        socket!!.send(broadcastPacket)
                        android.util.Log.d(TAG, "Sent client broadcast")
                    } catch (e: Exception) {
                        android.util.Log.w(TAG, "Broadcast send error: ${e.message}")
                    }

                    try {
                        val packet = DatagramPacket(buffer, buffer.size)
                        socket?.receive(packet)
                        val message = String(packet.data, 0, packet.length)

                        android.util.Log.d(TAG, "Received: $message from ${packet.address.hostAddress}")

                        if (message.trim() == BROADCAST_MESSAGE) {
                            val serverIp = packet.address.hostAddress ?: continue
                            android.util.Log.d(TAG, "Found ExtendScreen Server at $serverIp")
                            sendDeviceInfo(serverIp)
                        }
                    } catch (e: java.net.SocketTimeoutException) {
                        // 超时继续等待
                        continue
                    } catch (e: Exception) {
                        if (isScanning) {
                            android.util.Log.e(TAG, "Receive error: ${e.message}")
                        }
                    }
                }
            } catch (e: Exception) {
                android.util.Log.e(TAG, "Discovery bind error: ${e.message}")
            }
        }
    }

    private fun sendDeviceInfo(serverIp: String) {
        try {
            val deviceName = android.os.Build.MODEL
            val metrics = android.content.res.Resources.getSystem().displayMetrics
            val info = "DEVICE_INFO|$deviceName|${metrics.widthPixels}|${metrics.heightPixels}"

            val responseSocket = DatagramSocket()
            val addr = InetAddress.getByName(serverIp)
            val data = info.toByteArray()
            val packet = DatagramPacket(data, data.size, addr, RESPONSE_PORT)
            responseSocket.send(packet)
            responseSocket.close()

            android.util.Log.d(TAG, "Sent device info to $serverIp:$RESPONSE_PORT")

            onServerFound?.invoke(
                ServerInfo(ipAddress = serverIp, hostname = serverIp)
            )
        } catch (e: Exception) {
            android.util.Log.e(TAG, "Send device info error: ${e.message}")
        }
    }

    fun stop() {
        isScanning = false
        job?.cancel()
        try { socket?.close() } catch (_: Exception) {}
        socket = null
    }
}
