import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:equatable/equatable.dart';
import 'package:firebase_analytics/firebase_analytics.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import 'models/post.dart';

sealed class PostsEvent extends Equatable {
  const PostsEvent();

  @override
  List<Object?> get props => [];
}

class WatchPosts extends PostsEvent {
  const WatchPosts();
}

class WatchUser extends PostsEvent {
  const WatchUser();
}

class ToggleFavorites extends PostsEvent {
  const ToggleFavorites();
}

class SetFav extends PostsEvent {
  const SetFav(this.id, this.on);
  final String id;
  final bool on;

  @override
  List<Object?> get props => [id, on];
}

class DeletePost extends PostsEvent {
  const DeletePost(this.id);
  final String id;

  @override
  List<Object?> get props => [id];
}

class SavePost extends PostsEvent {
  const SavePost(this.post);
  final PostItem post;

  @override
  List<Object?> get props => [post];
}

enum Status { loading, success, error }

class PostsState extends Equatable {
  const PostsState({
    this.status = Status.loading,
    this.name = '',
    this.isAdmin = false,
    this.onlyFavorites = false,
    this.favs = const [],
    this.posts = const [],
    this.error = '',
  });

  final Status status;
  final String name;
  final bool isAdmin;
  final bool onlyFavorites;
  final List<String> favs;
  final List<PostItem> posts;
  final String error;

  List<PostItem> get visible => posts.where((p) => !onlyFavorites || favs.contains(p.id)).toList();

  PostsState copyWith({
    Status? status,
    String? name,
    bool? isAdmin,
    bool? onlyFavorites,
    List<String>? favs,
    List<PostItem>? posts,
    String? error,
  }) => PostsState(
    status: status ?? this.status,
    name: name ?? this.name,
    isAdmin: isAdmin ?? this.isAdmin,
    onlyFavorites: onlyFavorites ?? this.onlyFavorites,
    favs: favs ?? this.favs,
    posts: posts ?? this.posts,
    error: error ?? this.error,
  );

  @override
  List<Object?> get props => [status, name, isAdmin, onlyFavorites, favs, posts, error];
}

class PostsBloc extends Bloc<PostsEvent, PostsState> {
  PostsBloc(this.user, [FirebaseFirestore? db])
    : _db = db ?? FirebaseFirestore.instance,
      super(const PostsState()) {
    on<WatchPosts>((e, emit) => emit.forEach(
      _posts.snapshots(includeMetadataChanges: true),
      onData: (s) => state.copyWith(status: Status.success, posts: s.docs.map(PostItem.fromDoc).toList()),
      onError: (err, _) => state.copyWith(status: Status.error, error: '$err'),
    ));
    on<WatchUser>((e, emit) => emit.forEach(_user.snapshots(), onData: (s) {
      if (!s.exists && !s.metadata.isFromCache) {
        _user.set({'name': user.displayName ?? user.email ?? '', 'email': user.email ?? '', 'role': 'user', 'favs': []});
      }
      final d = s.data() ?? {};
      return state.copyWith(name: d['name'], isAdmin: d['role'] == 'admin', favs: List<String>.from(d['favs'] ?? []));
    }));
    on<ToggleFavorites>((e, emit) => emit(state.copyWith(onlyFavorites: !state.onlyFavorites)));
    on<SetFav>((e, emit) {
      _user.update({'favs': e.on ? FieldValue.arrayUnion([e.id]) : FieldValue.arrayRemove([e.id])});
      if (e.on) FirebaseAnalytics.instance.logEvent(name: 'like_post', parameters: {'post_id': e.id});
    });
    on<DeletePost>((e, emit) {
      _posts.doc(e.id).delete();
      _user.update({'favs': FieldValue.arrayRemove([e.id])});
      FirebaseAnalytics.instance.logEvent(name: 'delete_post', parameters: {'post_id': e.id});
    });
    on<SavePost>((e, emit) {
      emit(state.copyWith(status: Status.loading));
      final p = e.post;
      if (p.username.isEmpty || p.captionPrefix.isEmpty || p.imageAssets.isEmpty) {
        return emit(state.copyWith(status: Status.error, error: 'Fill username, description and pick a photo'));
      }
      p.id.isEmpty
          ? _posts.add({...p.toMap(), 'ownerId': user.uid, 'createdAt': FieldValue.serverTimestamp()})
          : _posts.doc(p.id).update(p.toMap());
      FirebaseAnalytics.instance.logEvent(name: p.id.isEmpty ? 'create_post' : 'edit_post');
      emit(state.copyWith(status: Status.success));
    });
  }

  final User user;
  final FirebaseFirestore _db;
  late final _posts = _db.collection('posts');
  late final _user = _db.collection('users').doc(user.uid);
}
