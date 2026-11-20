import 'package:firebase_auth/firebase_auth.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:messenger/main.dart' as app;

// flutter test integration_test --dart-define=ADMIN_EMAIL=... --dart-define=ADMIN_PASS=...
//   --dart-define=USER_EMAIL=... --dart-define=USER_PASS=...
const adminEmail = String.fromEnvironment('ADMIN_EMAIL');
const adminPass = String.fromEnvironment('ADMIN_PASS');
const userEmail = String.fromEnvironment('USER_EMAIL');
const userPass = String.fromEnvironment('USER_PASS');

Future<void> waitFor(WidgetTester t, Finder f) async {
  for (var i = 0; i < 100 && f.evaluate().isEmpty; i++) {
    await t.pump(const Duration(milliseconds: 200));
  }
  expect(f, findsWidgets);
}

Future<void> launchAndLogin(WidgetTester t, String email, String pass) async {
  await Firebase.initializeApp();
  await FirebaseAuth.instance.signOut();
  await app.main();
  await waitFor(t, find.text('Sign in').first);
  await t.enterText(find.widgetWithText(TextField, 'Email'), email);
  await t.enterText(find.widgetWithText(TextField, 'Password'), pass);
  await t.tap(find.widgetWithText(ElevatedButton, 'Sign in'));
  await waitFor(t, find.byIcon(Icons.storage_outlined));
}

Future<void> openPosts(WidgetTester t) async {
  await t.tap(find.byIcon(Icons.storage_outlined));
  await t.pumpAndSettle();
}

Future<String> addPost(WidgetTester t) async {
  final name = 'it_${DateTime.now().millisecondsSinceEpoch}';
  await t.tap(find.byIcon(Icons.add));
  await t.pumpAndSettle();
  await t.enterText(find.widgetWithText(TextField, 'Username'), name);
  await t.enterText(
    find.widgetWithText(TextField, 'Description'),
    'integration test',
  );
  await t.tap(find.widgetWithText(FilterChip, 'photo_1.jpg'));
  await t.pump();
  await t.tap(find.text('Save'));
  await waitFor(t, find.text(name));
  return name;
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('admin: adds a post and can delete it', (t) async {
    await launchAndLogin(t, adminEmail, adminPass);
    await openPosts(t);
    final name = await addPost(t);

    await t.tap(find.byIcon(Icons.delete).first);
    for (var i = 0; i < 50 && find.text(name).evaluate().isNotEmpty; i++) {
      await t.pump(const Duration(milliseconds: 200));
    }
    expect(find.text(name), findsNothing);
  });

  testWidgets('user: likes a post and sees it in favorites', (t) async {
    await launchAndLogin(t, userEmail, userPass);
    await openPosts(t);
    final name = await addPost(t);

    await t.tap(find.text(name));
    await t.pumpAndSettle();
    await t.dragUntilVisible(
      find.text('Like'),
      find.byType(ListView),
      const Offset(0, -300),
    );
    await t.tap(find.text('Like'));
    await t.pump();
    await t.tap(find.byKey(const Key('postDetailBackButton')));
    await t.pumpAndSettle();

    await t.tap(find.byIcon(Icons.favorite_border));
    await waitFor(t, find.byIcon(Icons.favorite));
    expect(find.text(name), findsOneWidget);
  });

  testWidgets('account: shows info about current user', (t) async {
    await launchAndLogin(t, userEmail, userPass);
    await t.tap(find.byIcon(Icons.person_outline));
    await t.pumpAndSettle();

    expect(find.text('Account'), findsOneWidget);
    expect(find.text(userEmail), findsOneWidget);
    expect(find.text(FirebaseAuth.instance.currentUser!.uid), findsOneWidget);
    expect(find.text('password'), findsOneWidget);
  });
}
