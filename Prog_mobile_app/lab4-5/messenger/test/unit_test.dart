import 'package:flutter_test/flutter_test.dart';
import 'package:messenger/data/models/post.dart';
import 'package:messenger/data/posts_bloc.dart';
import 'package:mockito/mockito.dart';

import 'mocks.dart';

void main() {
  late MockFirestore db;
  late MockCollection posts;
  late MockCollection users;
  late MockDocRef userDoc;

  setUp(() {
    db = MockFirestore();
    posts = MockCollection();
    users = MockCollection();
    userDoc = MockDocRef();
    when(db.collection('posts')).thenReturn(posts);
    when(db.collection('users')).thenReturn(users);
    when(users.doc('u1')).thenReturn(userDoc);
  });

  PostsBloc bloc() => PostsBloc(fakeUser(), db);

  test('PostsState.visible shows only favorites when filter is on', () {
    final state = PostsState(
      posts: [post('1'), post('2')],
      favs: const ['2'],
      onlyFavorites: true,
    );
    expect(state.visible.map((p) => p.id), ['2']);
  });

  test('PostItem.fromDoc maps Firestore document to PostItem', () {
    final doc = MockDocSnap();
    final meta = MockSnapMeta();
    when(meta.hasPendingWrites).thenReturn(true);
    when(doc.id).thenReturn('p1');
    when(doc.metadata).thenReturn(meta);
    when(doc.data()).thenReturn({...post('p1').toMap(), 'ownerId': 'u1'});
    final p = PostItem.fromDoc(doc);
    expect(p.id, 'p1');
    expect(p.ownerId, 'u1');
    expect(p.pending, isTrue);
    expect(p.username, 'userp1');
  });

  test('WatchUser reads role and favorites from users/{uid}', () async {
    final snap = MockDocSnap();
    final meta = MockSnapMeta();
    when(meta.isFromCache).thenReturn(false);
    when(snap.exists).thenReturn(true);
    when(snap.metadata).thenReturn(meta);
    when(snap.data()).thenReturn({
      'name': 'Ann',
      'role': 'admin',
      'favs': ['1'],
    });
    when(userDoc.snapshots()).thenAnswer((_) => Stream.value(snap));

    final b = bloc();
    b.add(const WatchUser());
    final state = await b.stream.first;
    expect(state.name, 'Ann');
    expect(state.isAdmin, isTrue);
    expect(state.favs, ['1']);
    await b.close();
  });

  test('SavePost with empty fields emits error and does not write', () async {
    final b = bloc();
    final states = expectLater(
      b.stream,
      emitsInOrder([
        isA<PostsState>().having((s) => s.status, 'status', Status.loading),
        isA<PostsState>().having((s) => s.status, 'status', Status.error),
      ]),
    );
    b.add(SavePost(post('').copyEmpty()));
    await states;
    verifyNever(posts.add(any as dynamic));
    await b.close();
  });

  test('SetFav(off) removes id from users/{uid}.favs', () async {
    when(userDoc.update(any as dynamic)).thenAnswer((_) async {});
    final b = bloc();
    b.add(const SetFav('1', false));
    await Future<void>.delayed(Duration.zero);
    final data =
        verify(userDoc.update(captureAny as dynamic)).captured.single as Map;
    expect(data.keys, ['favs']);
    await b.close();
  });

  test('ToggleFavorites flips onlyFavorites', () async {
    final b = bloc();
    b.add(const ToggleFavorites());
    expect((await b.stream.first).onlyFavorites, isTrue);
    await b.close();
  });
}

extension on PostItem {
  PostItem copyEmpty() => PostItem(
    avatarAsset: avatarAsset,
    username: '',
    meta: '',
    captionPrefix: '',
    captionMention: '',
    captionSuffix: '',
    imageAssets: const [],
  );
}
