import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/posts_bloc.dart';
import '../data/presence.dart';

class AccountScreen extends StatelessWidget {
  const AccountScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final user = FirebaseAuth.instance.currentUser!;
    return Scaffold(
      appBar: AppBar(title: const Text('Account')),
      body: ListView(
        children: [
          ListTile(title: const Text('UID'), subtitle: Text(user.uid)),
          ListTile(title: const Text('Email'), subtitle: Text(user.email ?? '-')),
          ListTile(title: const Text('Name'), subtitle: Text(user.displayName ?? '-')),
          ListTile(
            title: const Text('Providers'),
            subtitle: Text(user.providerData.map((p) => p.providerId).join(', ')),
          ),
          ListTile(title: const Text('Email verified'), subtitle: Text('${user.emailVerified}')),
          ListTile(title: const Text('Created'), subtitle: Text('${user.metadata.creationTime}')),
          ListTile(title: const Text('Last sign in'), subtitle: Text('${user.metadata.lastSignInTime}')),
          ListTile(
            title: const Text('Role'),
            subtitle: Text(context.select((PostsBloc b) => b.state.isAdmin) ? 'admin' : 'user'),
          ),
          StreamBuilder(
            stream: Presence.ref(user.uid).onValue,
            builder: (_, snap) {
              final s = snap.data?.snapshot.value as Map?;
              final seen = s == null ? '-' : DateTime.fromMillisecondsSinceEpoch(s['lastSeen'] as int);
              return ListTile(
                title: const Text('Status (Realtime Database)'),
                subtitle: Text('${s?['online'] == true ? 'online' : 'offline'}, last seen: $seen'),
              );
            },
          ),
          Padding(
            padding: const EdgeInsets.all(16),
            child: ElevatedButton(
              onPressed: Presence.signOut,
              child: const Text('Sign out'),
            ),
          ),
        ],
      ),
    );
  }
}
