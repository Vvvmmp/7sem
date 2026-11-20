import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:messenger/data/config_bloc.dart';
import 'package:messenger/data/posts_bloc.dart';
import 'package:messenger/screens/post_detail_screen.dart';
import 'package:messenger/screens/post_form_screen.dart';
import 'package:messenger/screens/posts_screen.dart';
import 'package:mockito/mockito.dart';

import 'mocks.dart';

Widget app(
  Widget child, {
  PostsBloc? posts,
  ConfigBloc? config,
}) => MultiBlocProvider(
  providers: [
    BlocProvider<PostsBloc>.value(
      value: posts ?? fakePostsBloc(const PostsState(status: Status.success)),
    ),
    BlocProvider<ConfigBloc>.value(
      value: config ?? fakeConfigBloc(const ConfigState(true, Colors.white)),
    ),
  ],
  child: MaterialApp(home: child),
);

void main() {
  testWidgets('form: enterText + tap sends SavePost with typed values', (
    t,
  ) async {
    final bloc = fakePostsBloc(const PostsState(status: Status.success));
    await t.pumpWidget(app(const PostFormScreen(), posts: bloc));

    await t.enterText(find.widgetWithText(TextField, 'Username'), 'alice');
    await t.enterText(find.widgetWithText(TextField, 'Description'), 'hello');
    await t.tap(find.widgetWithText(FilterChip, 'photo_1.jpg'));
    await t.pump();
    await t.tap(find.text('Save'));

    final e =
        verify(bloc.add(captureAny as dynamic)).captured.single as SavePost;
    expect(e.post.username, 'alice');
    expect(e.post.captionPrefix, 'hello');
    expect(e.post.imageAssets, ['assets/images/photo_1.jpg']);
  });

  testWidgets('form: tap on chip toggles its selection', (t) async {
    await t.pumpWidget(app(const PostFormScreen()));
    final chip = find.widgetWithText(FilterChip, 'photo_2.jpg');
    expect(t.widget<FilterChip>(chip).selected, isFalse);
    await t.tap(chip);
    await t.pump();
    expect(t.widget<FilterChip>(chip).selected, isTrue);
  });

  testWidgets('detail: tap Like switches label to Liked', (t) async {
    await t.pumpWidget(app(PostDetailScreen(post: post('1'))));
    expect(find.text('Like'), findsOneWidget);
    final like = find.byKey(const Key('postDetailLikeButton'));
    await t.dragUntilVisible(
      like,
      find.byType(ListView),
      const Offset(0, -300),
    );
    await t.tap(like);
    await t.pump();
    expect(find.text('Liked'), findsOneWidget);
  });

  testWidgets(
    'detail: Like button is disabled when remote config turns it off',
    (t) async {
      await t.pumpWidget(
        app(
          PostDetailScreen(post: post('1')),
          config: fakeConfigBloc(const ConfigState(false, Colors.white)),
        ),
      );
      final like = find.byKey(const Key('postDetailLikeButton'));
      await t.dragUntilVisible(
        like,
        find.byType(ListView),
        const Offset(0, -300),
      );
      expect(t.widget<ButtonStyleButton>(like).onPressed, isNull);
    },
  );

  testWidgets('posts list: drag scrolls down to the last post', (t) async {
    final state = PostsState(
      status: Status.success,
      posts: [for (var i = 1; i <= 12; i++) post('$i')],
    );
    await t.pumpWidget(app(const PostsScreen(), posts: fakePostsBloc(state)));
    expect(find.text('user12'), findsNothing);

    await t.dragUntilVisible(
      find.text('user12'),
      find.byType(ListView),
      const Offset(0, -300),
    );
    expect(find.text('user12'), findsOneWidget);
  });

  testWidgets('posts list: tap on heart sends ToggleFavorites', (t) async {
    final bloc = fakePostsBloc(
      PostsState(status: Status.success, posts: [post('1')]),
    );
    await t.pumpWidget(app(const PostsScreen(), posts: bloc));
    await t.tap(find.byIcon(Icons.favorite_border));
    verify(bloc.add(const ToggleFavorites())).called(1);
  });
}
