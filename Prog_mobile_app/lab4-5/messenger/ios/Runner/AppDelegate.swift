import Flutter
import UIKit

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)

    let batteryChannel = FlutterMethodChannel(
      name: "com.example.messenger/battery",
      binaryMessenger: engineBridge.applicationRegistrar.messenger()
    )
    batteryChannel.setMethodCallHandler { call, result in
      guard call.method == "getBatteryLevel" else {
        result(FlutterMethodNotImplemented)
        return
      }
      let device = UIDevice.current
      device.isBatteryMonitoringEnabled = true
      if device.batteryState == .unknown {
        result(FlutterError(code: "UNAVAILABLE", message: "Battery level not available.", details: nil))
      } else {
        result(Int(device.batteryLevel * 100))
      }
    }
  }
}
