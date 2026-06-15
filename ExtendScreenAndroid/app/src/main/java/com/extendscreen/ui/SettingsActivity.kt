package com.extendscreen.ui

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.Bundle
import android.os.IBinder
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.Spinner
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import com.extendscreen.ConnectionState
import com.extendscreen.R
import com.extendscreen.service.StreamService

/**
 * 设置页 Activity — 分辨率/帧率选择、查看连接状态
 */
class SettingsActivity : AppCompatActivity() {

    private var streamService: StreamService? = null
    private var isBound = false

    private lateinit var spinnerResolution: Spinner
    private lateinit var spinnerFps: Spinner
    private lateinit var tvStatus: TextView
    private lateinit var btnDisconnect: Button

    private val serviceConnection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, service: IBinder?) {
            val binder = service as StreamService.StreamBinder
            streamService = binder.getService()
            isBound = true
            updateStatus()
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            streamService = null
            isBound = false
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_settings)

        spinnerResolution = findViewById(R.id.spinner_resolution)
        spinnerFps = findViewById(R.id.spinner_fps)
        tvStatus = findViewById(R.id.tv_connection_status)
        btnDisconnect = findViewById(R.id.btn_disconnect)

        // 分辨率选项
        val resolutionAdapter = ArrayAdapter(
            this, android.R.layout.simple_spinner_item,
            arrayOf("自适应", "1280x720", "1920x1080", "2560x1440")
        )
        resolutionAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        spinnerResolution.adapter = resolutionAdapter

        // 帧率选项
        val fpsAdapter = ArrayAdapter(
            this, android.R.layout.simple_spinner_item,
            arrayOf("30 fps", "60 fps")
        )
        fpsAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        spinnerFps.adapter = fpsAdapter

        btnDisconnect.setOnClickListener {
            streamService?.disconnect()
            updateStatus()
        }

        // 绑定服务
        bindService(
            Intent(this, StreamService::class.java),
            serviceConnection,
            Context.BIND_AUTO_CREATE
        )
    }

    private fun updateStatus() {
        val status = streamService?.connectionState
        when (status) {
            ConnectionState.CONNECTED -> {
                tvStatus.text = "已连接 — ${streamService?.connectedServer?.hostname ?: "电脑"}"
                btnDisconnect.isEnabled = true
            }
            ConnectionState.CONNECTING -> {
                tvStatus.text = "正在连接..."
                btnDisconnect.isEnabled = false
            }
            else -> {
                tvStatus.text = "未连接"
                btnDisconnect.isEnabled = false
            }
        }
    }

    override fun onDestroy() {
        if (isBound) {
            unbindService(serviceConnection)
        }
        super.onDestroy()
    }
}
