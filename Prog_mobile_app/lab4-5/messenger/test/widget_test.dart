import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hive/hive.dart';

import 'package:messenger/data/hive_store.dart';
import 'package:messenger/main.dart';
import 'package:messenger/screens/feed_screen.dart';
import 'package:messenger/screens/post_detail_screen.dart';
import 'package:messenger/screens/story_viewer_screen.dart';
import 'package:messenger/widgets/story_avatar_row.dart';

void main() {
  setUpAll(() => HiveStore.init(Directory.systemTemp.createTempSync().path, Hive.generateSecureKey()));

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

  testWidgets('Post detail returns like state via pop', (tester) async {
    await pumpAtPhoneSize(tester);

    await tester.tap(find.text('Nilesh').first);
    await tester.pumpAndSettle();
    expect(find.byType(PostDetailScreen), findsOneWidget);

    await tester.scrollUntilVisible(find.byKey(const Key('postDetailLikeButton')), 200);
    await tester.tap(find.byKey(const Key('postDetailLikeButton')));
    await tester.tap(find.byKey(const Key('postDetailBackButton')));
    await tester.pumpAndSettle();

    expect(find.text("You liked Nilesh's post"), findsOneWidget);
  });

  testWidgets('Tapping a story opens viewer with swipeable PageView', (tester) async {
    await pumpAtPhoneSize(tester);

    // Index 0 is the "Your Story" avatar, index 1 is Nilesh's story card.
    await tester.tap(find.descendant(of: find.byType(StoryAvatarRow), matching: find.byType(Image)).at(1));
    await tester.pumpAndSettle();
    expect(find.byType(StoryViewerScreen), findsOneWidget);
    expect(find.text('1/5'), findsOneWidget);

    await tester.fling(find.byType(PageView), const Offset(-300, 0), 1000);
    await tester.pumpAndSettle();
    expect(find.text('2/5'), findsOneWidget);
  });
}
