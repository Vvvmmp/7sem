import 'package:flutter/material.dart';

import 'screens/feed_screen.dart';
import 'theme/app_theme.dart';

void main() {
  runApp(const MessengerApp());
}

class MessengerApp extends StatelessWidget {
  const MessengerApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Messenger',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.theme,
      home: const FeedScreen(),
    );
  }
}
