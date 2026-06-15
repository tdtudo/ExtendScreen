package com.extendscreen.network

import android.util.Log
import com.extendscreen.ServerInfo
import kotlinx.coroutines.*
import java.io.OutputStream
import java.net.Socket

/**
 * TCP 触控输入发送端：将安卓触摸事件发送到电脑
 */
class InputSender {

    companion object {
        private const val TAG = "InputSender"
    }

    private var socket: Socket? = null
    private var outputStream: OutputStream? = null
    private var isConnected = false
    private var sendJob: Job? = null
    private val sendQueue = java.util.concurrent.ConcurrentLinkedQueue<ByteArray>()

    fun connect(server: ServerInfo, scope: CoroutineScope) {
        sendJob = scope.launch(Dispatchers.IO) {
            try {
                Log.d(TAG, "Connecting input to ${server.ipAddress}:${server.inputPort}")
                socket = Socket(server.ipAddress, server.inputPort)
                socket?.tcpNoDelay = true
                outputStream = socket?.getOutputStream()
                isConnected = true
                Log.d(TAG, "Input sender connected")

                // 发送队列中的事件
                while (isConnected && isActive) {
                    val data = sendQueue.poll()
                    if (data != null && outputStream != null) {
                        try {
                            outputStream?.write(data)
                            outputStream?.flush()
                        } catch (e: Exception) {
                            Log.e(TAG, "Send error: ${e.message}")
                            break
                        }
                    } else {
                        delay(5) // 避免忙等
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Input connection error: ${e.message}")
            }
        }
    }

    fun sendTouchEvent(type: String, x: Float, y: Float, action: Int, pointerCount: Int) {
        val message = "$type|$x|$y|0|0|1.0|$pointerCount|$action|${System.currentTimeMillis()}"
        sendQueue.offer(message.toByteArray())
    }

    fun sendScrollEvent(deltaX: Float, deltaY: Float) {
        val message = "Scroll|0|0|$deltaX|$deltaY|1.0|2|1|${System.currentTimeMillis()}"
        sendQueue.offer(message.toByteArray())
    }

    fun sendZoomEvent(scale: Float) {
        val message = "Zoom|0|0|0|0|$scale|2|1|${System.currentTimeMillis()}"
        sendQueue.offer(message.toByteArray())
    }

    fun disconnect() {
        isConnected = false
        sendJob?.cancel()
        try {
            outputStream?.close()
            socket?.close()
        } catch (e: Exception) {
            Log.e(TAG, "Close error: ${e.message}")
        }
    }
}
