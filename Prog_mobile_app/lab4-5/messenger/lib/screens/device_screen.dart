import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';

import '../theme/app_colors.dart';
import '../theme/app_text_styles.dart';
import 'feed_screen.dart';

class DeviceScreen extends StatefulWidget {
  const DeviceScreen({super.key});

  @override
  State<DeviceScreen> createState() => _DeviceScreenState();
}

class _DeviceScreenState extends State<DeviceScreen> {
  static const _deviceChannel = MethodChannel('com.example.messenger/device');
  static const _batteryChannel = MethodChannel('com.example.messenger/battery');

  final _phoneController = TextEditingController(text: '+375291234567');
  final _bodyController = TextEditingController(text: 'Hello from Messenger!');

  String _locale = '—';
  String _battery = '—';
  File? _photo;

  @override
  void dispose() {
    _phoneController.dispose();
    _bodyController.dispose();
    super.dispose();
  }

  Future<void> _getLocale() async {
    try {
      final result = await _deviceChannel.invokeMapMethod<String, String>('getLocale');
      setState(() => _locale = '${result!['tag']} (${result['name']})');
    } on PlatformException catch (e) {
      setState(() => _locale = 'Error: ${e.message}');
    }
  }

  Future<void> _sendSms() async {
    try {
      await _deviceChannel.invokeMethod('sendSms', {
        'phone': _phoneController.text,
        'body': _bodyController.text,
      });
    } on PlatformException catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Error: ${e.message}')));
    }
  }

  Future<void> _getBattery() async {
    try {
      final level = await _batteryChannel.invokeMethod<int>('getBatteryLevel');
      setState(() => _battery = '${Platform.operatingSystem}: $level%');
    } on PlatformException catch (e) {
      setState(() => _battery = 'Error: ${e.message}');
    }
  }

  Future<void> _takePhoto() async {
    final image = await ImagePicker().pickImage(source: ImageSource.camera);
    if (image != null) setState(() => _photo = File(image.path));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.surface,
      appBar: AppBar(
        backgroundColor: AppColors.surface,
        title: Text('Device', style: AppTextStyles.sectionHeader),
        actions: [
          IconButton(
            icon: const Icon(Icons.home_filled, color: AppColors.textPrimary),
            onPressed: () => Navigator.pushAndRemoveUntil(
              context,
              MaterialPageRoute(builder: (_) => const FeedScreen()),
              (route) => false,
            ),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text('Device locale', style: AppTextStyles.sectionHeader),
          Text(_locale, style: AppTextStyles.captionText),
          ElevatedButton(onPressed: _getLocale, child: const Text('Get locale')),
          const Divider(height: 32),
          Text('Send SMS', style: AppTextStyles.sectionHeader),
          TextField(
            controller: _phoneController,
            keyboardType: TextInputType.phone,
            decoration: const InputDecoration(labelText: 'Phone'),
          ),
          TextField(
            controller: _bodyController,
            decoration: const InputDecoration(labelText: 'Message'),
          ),
          const SizedBox(height: 8),
          ElevatedButton(onPressed: _sendSms, child: const Text('Open SMS app')),
          const Divider(height: 32),
          Text('Battery level', style: AppTextStyles.sectionHeader),
          Text(_battery, style: AppTextStyles.captionText),
          ElevatedButton(onPressed: _getBattery, child: const Text('Get battery level')),
          const Divider(height: 32),
          Text('Camera', style: AppTextStyles.sectionHeader),
          if (_photo != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: ClipRRect(borderRadius: BorderRadius.circular(16), child: Image.file(_photo!)),
            ),
          ElevatedButton(onPressed: _takePhoto, child: const Text('Take photo')),
        ],
      ),
    );
  }
}
