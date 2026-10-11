import 'package:flutter/material.dart';

import 'data/models/user_profile.dart';
import 'screens/device_screen.dart';
import 'screens/feed_screen.dart';
import 'screens/profile_screen.dart';
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
      onGenerateRoute: (settings) {
        switch (settings.name) {
          case '/profile':
            return MaterialPageRoute(
              settings: settings,
              builder: (_) => ProfileScreen(profile: settings.arguments as UserProfile),
            );
          case '/device':
            return MaterialPageRoute(settings: settings, builder: (_) => const DeviceScreen());
        }
        return null;
      },
    );
  }
}
