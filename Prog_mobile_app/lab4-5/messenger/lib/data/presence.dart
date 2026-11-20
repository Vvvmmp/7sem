import 'dart:async';

import 'package:firebase_analytics/firebase_analytics.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:firebase_database/firebase_database.dart';
import 'package:flutter/widgets.dart';

class Presence {
  Presence._();

  static StreamSubscription? _sub;

  static DatabaseReference ref(String uid) => FirebaseDatabase.instance.ref('status/$uid');

  static Map<String, Object> _status(bool online) => {'online': online, 'lastSeen': ServerValue.timestamp};

  static void set(bool online) {
    final uid = FirebaseAuth.instance.currentUser?.uid;
    if (uid != null) ref(uid).set(_status(online));
  }

  static void start() {
    AppLifecycleListener(onResume: () => set(true), onPause: () => set(false));
    FirebaseAuth.instance.authStateChanges().listen((user) {
      _sub?.cancel();
      if (user == null) return;
      _sub = FirebaseDatabase.instance.ref('.info/connected').onValue.listen((e) {
        if (e.snapshot.value != true) return;
        ref(user.uid).onDisconnect().set(_status(false));
        set(true);
      });
    });
  }

  static Future<void> signOut() {
    set(false);
    FirebaseAnalytics.instance.logEvent(name: 'logout');
    return FirebaseAuth.instance.signOut();
  }
}
