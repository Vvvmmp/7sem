import 'package:firebase_analytics/firebase_analytics.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _auth = FirebaseAuth.instance;
  final _analytics = FirebaseAnalytics.instance;
  final _email = TextEditingController();
  final _password = TextEditingController();

  void _check({bool password = true}) {
    if (_email.text.isEmpty || (password && _password.text.isEmpty)) {
      throw FirebaseAuthException(code: 'empty-fields', message: 'Enter email${password ? ' and password' : ''}');
    }
  }

  Future<void> _run(Future<void> Function() action, [String? done]) async {
    try {
      await action();
      if (done != null && mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(done)));
    } on FirebaseAuthException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message ?? e.code)));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Sign in')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          TextField(controller: _email, decoration: const InputDecoration(labelText: 'Email')),
          TextField(controller: _password, obscureText: true, decoration: const InputDecoration(labelText: 'Password')),
          const SizedBox(height: 16),
          ElevatedButton(
            onPressed: () => _run(() async {
              _check();
              await _auth.signInWithEmailAndPassword(email: _email.text, password: _password.text);
              await _analytics.logLogin(loginMethod: 'password');
            }),
            child: const Text('Sign in'),
          ),
          OutlinedButton(
            onPressed: () => _run(() async {
              _check();
              await _auth.createUserWithEmailAndPassword(email: _email.text, password: _password.text);
              await _analytics.logSignUp(signUpMethod: 'password');
            }),
            child: const Text('Register'),
          ),
          TextButton(
            onPressed: () => _run(() async {
              _check(password: false);
              await _auth.sendPasswordResetEmail(email: _email.text);
              await _analytics.logEvent(name: 'password_reset');
            }, 'Reset link sent to ${_email.text}'),
            child: const Text('Forgot password?'),
          ),
          const Divider(),
          OutlinedButton.icon(
            onPressed: () => _run(() async {
              await _auth.signInWithProvider(GithubAuthProvider());
              await _analytics.logLogin(loginMethod: 'github');
            }),
            icon: const Icon(Icons.code),
            label: const Text('Sign in with GitHub'),
          ),
        ],
      ),
    );
  }
}
