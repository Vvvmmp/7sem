import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:messenger/data/config_bloc.dart';
import 'package:messenger/data/models/post.dart';
import 'package:messenger/data/posts_bloc.dart';
import 'package:mockito/annotations.dart';
import 'package:mockito/mockito.dart';

import 'mocks.mocks.dart';

export 'mocks.mocks.dart';

@GenerateNiceMocks([
  MockSpec<FirebaseFirestore>(as: #MockFirestore),
  MockSpec<CollectionReference<Map<String, dynamic>>>(as: #MockCollection),
  MockSpec<DocumentReference<Map<String, dynamic>>>(as: #MockDocRef),
  MockSpec<DocumentSnapshot<Map<String, dynamic>>>(as: #MockDocSnap),
  MockSpec<SnapshotMetadata>(as: #MockSnapMeta),
  MockSpec<User>(),
  MockSpec<PostsBloc>(),
  MockSpec<ConfigBloc>(),
])
void main() {}

MockUser fakeUser() {
  final u = MockUser();
  when(u.uid).thenReturn('u1');
  return u;
}

MockPostsBloc fakePostsBloc(PostsState state) {
  final b = MockPostsBloc();
  when(b.state).thenReturn(state);
  when(b.stream).thenAnswer((_) => const Stream<PostsState>.empty());
  final u = fakeUser();
  when(b.user).thenReturn(u);
  return b;
}

MockConfigBloc fakeConfigBloc(ConfigState state) {
  final b = MockConfigBloc();
  when(b.state).thenReturn(state);
  when(b.stream).thenAnswer((_) => const Stream<ConfigState>.empty());
  return b;
}

PostItem post(String id, {String owner = 'u1'}) => PostItem(
  id: id,
  ownerId: owner,
  avatarAsset: 'assets/images/avatar_me.jpg',
  username: 'user$id',
  meta: 'loc',
  captionPrefix: 'text $id',
  captionMention: '',
  captionSuffix: '',
  imageAssets: const ['assets/images/photo_1.jpg'],
);
