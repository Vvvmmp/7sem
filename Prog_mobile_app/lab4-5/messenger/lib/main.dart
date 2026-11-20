import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import 'data/config_bloc.dart';
import 'data/models/user_profile.dart';
import 'data/posts_bloc.dart';
import 'data/presence.dart';
import 'screens/device_screen.dart';
import 'screens/feed_screen.dart';
import 'screens/login_screen.dart';
import 'screens/profile_screen.dart';
import 'theme/app_theme.dart';

final messengerKey = GlobalKey<ScaffoldMessengerState>();

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Firebase.initializeApp();
  FirebaseFirestore.instance.settings = const Settings(persistenceEnabled: true);
  await ConfigBloc.init();
  await FirebaseMessaging.instance.requestPermission();
  FirebaseMessaging.onMessage.listen((m) {
    messengerKey.currentState?.showSnackBar(
      SnackBar(content: Text('${m.notification?.title}\n${m.notification?.body}')),
    );
  });
  Presence.start();
  runApp(const MessengerApp());
}

class MessengerApp extends StatelessWidget {
  const MessengerApp({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocProvider(
      create: (_) => ConfigBloc(),
      child: StreamBuilder<User?>(
        stream: FirebaseAuth.instance.authStateChanges(),
        initialData: FirebaseAuth.instance.currentUser,
        builder: (context, snap) {
          final user = snap.data;
          return BlocProvider(
            key: ValueKey(user?.uid),
            create: (_) => PostsBloc(user!)
              ..add(const WatchPosts())
              ..add(const WatchUser()),
            child: MaterialApp(
              title: 'Messenger',
              debugShowCheckedModeBanner: false,
              theme: AppTheme.theme,
              scaffoldMessengerKey: messengerKey,
              home: user == null ? const LoginScreen() : const FeedScreen(),
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
            ),
          );
        },
      ),
    );
  }
}
