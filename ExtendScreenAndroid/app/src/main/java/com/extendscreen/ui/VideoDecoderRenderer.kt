package com.extendscreen.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.widget.ImageView

/**
 * JPEG 解码与渲染器 — 后台线程解码，主线程渲染
 * 使用双缓冲避免闪烁：decodeBitmap 和 displayBitmap 交替使用
 */
object VideoDecoderRenderer {

    private const val TAG = "JpegRenderer"
    private var imageView: ImageView? = null
    private var displayBitmap: Bitmap? = null
    private val mainHandler = Handler(Looper.getMainLooper())

    fun setImageView(view: ImageView) {
        imageView = view
    }

    fun renderFrame(frameData: ByteArray, offset: Int = 0, length: Int = frameData.size) {
        try {
            val bitmap = BitmapFactory.decodeByteArray(frameData, offset, length)
            if (bitmap != null) {
                val oldBitmap = displayBitmap
                displayBitmap = bitmap

                mainHandler.post {
                    imageView?.setImageBitmap(bitmap)
                    // 回收旧 Bitmap（新 Bitmap 已经设置，不会闪烁）
                    oldBitmap?.recycle()
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Render error: ${e.message}")
        }
    }

    fun release() {
        displayBitmap?.recycle()
        displayBitmap = null
        imageView = null
    }
}
