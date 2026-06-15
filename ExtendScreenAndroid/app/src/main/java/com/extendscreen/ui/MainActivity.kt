package com.extendscreen.ui

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.Bundle
import android.os.IBinder
import android.view.*
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import com.extendscreen.ConnectionState
import com.extendscreen.R
import com.extendscreen.service.StreamService

class MainActivity : AppCompatActivity() {

    private var streamService: StreamService? = null
    private var isBound = false

    // Views
    private var homeView: View? = null
    private var wifiView: View? = null
    private var usbView: View? = null
    private var videoView: ImageView? = null
    private var btnDisconnect: Button? = null

    // Wi-Fi page
    private var tvStatus: TextView? = null
    private var etIp: EditText? = null
    private var btnConnect: Button? = null
    private var spinner: ProgressBar? = null

    // USB page
    private var tvUsbStatus: TextView? = null
    private var usbSpinner: ProgressBar? = null

    private var touchEnabled = false
    private var currentMode = "" // "wifi" or "usb"

    private var scaleDetector: ScaleGestureDetector? = null
    private var lastTouchX = 0f
    private var lastTouchY = 0f

    private val serviceConnection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, service: IBinder?) {
            val binder = service as StreamService.StreamBinder
            streamService = binder.getService()
            isBound = true

            streamService?.onStateChanged = { state ->
                runOnUiThread { updateUI(state) }
            }
        }

        override fun onServiceDisconnected(name: ComponentName?) {
            streamService = null
            isBound = false
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        // 所有页面
        homeView = findViewById(R.id.home_view)
        wifiView = findViewById(R.id.wifi_view)
        usbView = findViewById(R.id.usb_view)
        videoView = findViewById(R.id.video_view)
        btnDisconnect = findViewById(R.id.btn_disconnect)

        // Wi-Fi 页面控件
        tvStatus = findViewById(R.id.tv_status)
        etIp = findViewById(R.id.et_ip)
        btnConnect = findViewById(R.id.btn_connect)
        spinner = findViewById(R.id.connecting_spinner)

        // USB 页面控件
        tvUsbStatus = findViewById(R.id.tv_usb_status)
        usbSpinner = findViewById(R.id.usb_spinner)

        VideoDecoderRenderer.setImageView(videoView!!)

        // 主页按钮
        findViewById<Button>(R.id.btn_wifi_mode)?.setOnClickListener {
            currentMode = "wifi"
            showWifiPage()
            streamService?.startWifiDiscovery()
        }

        findViewById<Button>(R.id.btn_usb_mode)?.setOnClickListener {
            currentMode = "usb"
            showUsbPage()
        }

        // Wi-Fi 页面按钮
        btnConnect?.setOnClickListener {
            val ip = etIp?.text?.toString()?.trim() ?: ""
            if (ip.isNotEmpty()) {
                tvStatus?.text = "正在连接 $ip ..."
                streamService?.connectManual(ip)
            }
        }

        findViewById<Button>(R.id.btn_wifi_back)?.setOnClickListener {
            streamService?.stopDiscovery()
            showHomePage()
        }

        // USB 页面按钮
        findViewById<Button>(R.id.btn_usb_connect)?.setOnClickListener {
            tvUsbStatus?.text = "正在连接..."
            usbSpinner?.visibility = View.VISIBLE
            streamService?.connectUsb()
        }

        findViewById<Button>(R.id.btn_usb_back)?.setOnClickListener {
            showHomePage()
        }

        // 断开按钮
        btnDisconnect?.setOnClickListener {
            streamService?.disconnectAndRestart()
            showHomePage()
        }

        setupTouchInput()
        startAndBindService()
    }

    private fun showHomePage() {
        homeView?.visibility = View.VISIBLE
        wifiView?.visibility = View.GONE
        usbView?.visibility = View.GONE
        videoView?.visibility = View.GONE
        btnDisconnect?.visibility = View.GONE
        showSystemUI()
    }

    private fun showWifiPage() {
        homeView?.visibility = View.GONE
        wifiView?.visibility = View.VISIBLE
        usbView?.visibility = View.GONE
        videoView?.visibility = View.GONE
        btnDisconnect?.visibility = View.GONE
        tvStatus?.text = "正在搜索电脑..."
        spinner?.visibility = View.VISIBLE
        showSystemUI()
    }

    private fun showUsbPage() {
        homeView?.visibility = View.GONE
        wifiView?.visibility = View.GONE
        usbView?.visibility = View.VISIBLE
        videoView?.visibility = View.GONE
        btnDisconnect?.visibility = View.GONE
        tvUsbStatus?.text = ""
        usbSpinner?.visibility = View.GONE
        showSystemUI()
    }

    private fun startAndBindService() {
        val intent = Intent(this, StreamService::class.java)
        startService(intent)
        bindService(intent, serviceConnection, Context.BIND_AUTO_CREATE)
    }

    private fun updateUI(state: ConnectionState) {
        when (state) {
            ConnectionState.CONNECTING -> {
                if (currentMode == "wifi") {
                    wifiView?.visibility = View.VISIBLE
                    tvStatus?.text = "正在连接..."
                    spinner?.visibility = View.VISIBLE
                } else if (currentMode == "usb") {
                    usbView?.visibility = View.VISIBLE
                    tvUsbStatus?.text = "正在连接..."
                    usbSpinner?.visibility = View.VISIBLE
                }
                videoView?.visibility = View.GONE
                btnDisconnect?.visibility = View.GONE
                showSystemUI()
            }
            ConnectionState.CONNECTED -> {
                homeView?.visibility = View.GONE
                wifiView?.visibility = View.GONE
                usbView?.visibility = View.GONE
                videoView?.visibility = View.VISIBLE
                btnDisconnect?.visibility = View.VISIBLE
                touchEnabled = true
                window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                hideSystemUI()
            }
            ConnectionState.DISCONNECTED -> {
                window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                if (currentMode == "wifi") {
                    wifiView?.visibility = View.VISIBLE
                    tvStatus?.text = "连接断开，正在重新搜索..."
                    spinner?.visibility = View.VISIBLE
                } else if (currentMode == "usb") {
                    usbView?.visibility = View.VISIBLE
                    tvUsbStatus?.text = "连接失败，请重试"
                    usbSpinner?.visibility = View.GONE
                }
                videoView?.visibility = View.GONE
                btnDisconnect?.visibility = View.GONE
                touchEnabled = false
                showSystemUI()
            }
            ConnectionState.ERROR -> {
                window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                if (currentMode == "wifi") {
                    wifiView?.visibility = View.VISIBLE
                    tvStatus?.text = "连接失败，请重试"
                    spinner?.visibility = View.GONE
                } else if (currentMode == "usb") {
                    usbView?.visibility = View.VISIBLE
                    tvUsbStatus?.text = "连接失败，请重试"
                    usbSpinner?.visibility = View.GONE
                }
                videoView?.visibility = View.GONE
                btnDisconnect?.visibility = View.GONE
                showSystemUI()
            }
        }
    }

    private fun hideSystemUI() {
        window.decorView.systemUiVisibility = (
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                or View.SYSTEM_UI_FLAG_FULLSCREEN
                or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                )
    }

    private fun showSystemUI() {
        window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_VISIBLE
    }

    private fun setupTouchInput() {
        scaleDetector = ScaleGestureDetector(this, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
            override fun onScale(detector: ScaleGestureDetector): Boolean {
                if (!touchEnabled) return false
                streamService?.sendZoomEvent(detector.scaleFactor)
                return true
            }
        })

        videoView?.setOnTouchListener { _, event ->
            if (!touchEnabled) return@setOnTouchListener false

            scaleDetector?.onTouchEvent(event)

            val x = event.x / (videoView?.width ?: 1).toFloat()
            val y = event.y / (videoView?.height ?: 1).toFloat()

            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    lastTouchX = x; lastTouchY = y
                    if (event.pointerCount == 1)
                        streamService?.sendTouchEvent("Touch", x, y, 0, 1)
                }
                MotionEvent.ACTION_MOVE -> {
                    when (event.pointerCount) {
                        1 -> streamService?.sendTouchEvent("Touch", x, y, 1, 1)
                        2 -> {
                            streamService?.sendScrollEvent((x - lastTouchX) * 10f, (y - lastTouchY) * 10f)
                            lastTouchX = x; lastTouchY = y
                        }
                    }
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP ->
                    streamService?.sendTouchEvent("Touch", x, y, 2, 1)
            }
            true
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus && touchEnabled) {
            hideSystemUI()
        }
    }

    override fun onDestroy() {
        if (isBound) unbindService(serviceConnection)
        VideoDecoderRenderer.release()
        super.onDestroy()
    }
}
