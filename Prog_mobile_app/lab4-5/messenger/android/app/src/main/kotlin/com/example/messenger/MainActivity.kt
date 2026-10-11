package com.example.messenger

import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.os.BatteryManager
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    private val deviceChannel = "com.example.messenger/device"
    private val batteryChannel = "com.example.messenger/battery"

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        val messenger = flutterEngine.dartExecutor.binaryMessenger

        MethodChannel(messenger, deviceChannel).setMethodCallHandler { call, result ->
            when (call.method) {
                "getLocale" -> {
                    val locale = resources.configuration.locales[0]
                    result.success(mapOf("tag" to locale.toLanguageTag(), "name" to locale.displayName))
                }
                "sendSms" -> {
                    val phone = call.argument<String>("phone")
                    val body = call.argument<String>("body")
                    if (phone.isNullOrBlank() || body == null) {
                        result.error("INVALID_ARGS", "phone and body are required", null)
                        return@setMethodCallHandler
                    }
                    val intent = Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:$phone"))
                        .putExtra("sms_body", body)
                    try {
                        startActivity(intent)
                        result.success(null)
                    } catch (e: ActivityNotFoundException) {
                        result.error("NO_SMS_APP", "No app can send SMS", null)
                    }
                }
                else -> result.notImplemented()
            }
        }

        // Android: BatteryManager system service.
        MethodChannel(messenger, batteryChannel).setMethodCallHandler { call, result ->
            if (call.method == "getBatteryLevel") {
                val batteryManager = getSystemService(BATTERY_SERVICE) as BatteryManager
                val level = batteryManager.getIntProperty(BatteryManager.BATTERY_PROPERTY_CAPACITY)
                if (level != -1) result.success(level)
                else result.error("UNAVAILABLE", "Battery level not available.", null)
            } else {
                result.notImplemented()
            }
        }
    }
}
