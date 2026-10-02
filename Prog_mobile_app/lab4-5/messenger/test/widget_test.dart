import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:messenger/main.dart';
import 'package:messenger/screens/feed_screen.dart';

void main() {
  Future<void> pumpAtPhoneSize(WidgetTester tester) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(const MessengerApp());
    await tester.pumpAndSettle();
  }

  testWidgets('App launches on Feed screen with expected sections', (tester) async {
    await pumpAtPhoneSize(tester);

    expect(find.byType(FeedScreen), findsOneWidget);
    expect(find.text('Discover'), findsOneWidget);
    expect(find.text('Following'), findsOneWidget);
    expect(find.text('Recently Post'), findsOneWidget);
  });

  testWidgets('Tapping the profile avatar navigates to the Profile screen', (tester) async {
    await pumpAtPhoneSize(tester);

    await tester.tap(find.byKey(const Key('feedProfileAvatarButton')));
    await tester.pumpAndSettle();

    expect(find.text('Darlene Beats'), findsOneWidget);
  });
}
