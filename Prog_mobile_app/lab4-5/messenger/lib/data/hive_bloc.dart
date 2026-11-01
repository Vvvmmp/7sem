import 'package:equatable/equatable.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import 'hive_store.dart';
import 'models/post.dart';

sealed class HiveEvent extends Equatable {
  const HiveEvent();

  @override
  List<Object?> get props => [];
}

class LoadPosts extends HiveEvent {
  const LoadPosts();
}

class SelectUser extends HiveEvent {
  const SelectUser(this.name);
  final String name;

  @override
  List<Object?> get props => [name];
}

class ToggleFavorites extends HiveEvent {
  const ToggleFavorites();
}

class SetFav extends HiveEvent {
  const SetFav(this.key, this.on);
  final int key;
  final bool on;

  @override
  List<Object?> get props => [key, on];
}

class DeletePost extends HiveEvent {
  const DeletePost(this.key);
  final int key;

  @override
  List<Object?> get props => [key];
}

class SavePost extends HiveEvent {
  const SavePost(this.key, this.post);
  final int? key;
  final PostItem post;

  @override
  List<Object?> get props => [key, post];
}

enum Status { loading, success, error }

class HiveState extends Equatable {
  const HiveState({
    this.status = Status.loading,
    this.user = 'Admin',
    this.onlyFavorites = false,
    this.favs = const [],
    this.keys = const [],
    this.error = '',
  });

  final Status status;
  final String user;
  final bool onlyFavorites;
  final List<int> favs;
  final List<int> keys;
  final String error;

  bool get isAdmin => HiveStore.users.get(user)!.isAdmin;

  HiveState copyWith({
    Status? status,
    String? user,
    bool? onlyFavorites,
    List<int>? favs,
    List<int>? keys,
    String? error,
  }) => HiveState(
    status: status ?? this.status,
    user: user ?? this.user,
    onlyFavorites: onlyFavorites ?? this.onlyFavorites,
    favs: favs ?? this.favs,
    keys: keys ?? this.keys,
    error: error ?? this.error,
  );

  @override
  List<Object?> get props => [status, user, onlyFavorites, favs, keys, error];
}

class HiveBloc extends Bloc<HiveEvent, HiveState> {
  HiveBloc() : super(const HiveState()) {
    on<LoadPosts>((e, emit) => _run(emit, state, () => Future.delayed(const Duration(seconds: 1))));
    on<SelectUser>((e, emit) => _run(emit, state.copyWith(user: e.name)));
    on<ToggleFavorites>((e, emit) => _run(emit, state.copyWith(onlyFavorites: !state.onlyFavorites)));
    on<SetFav>((e, emit) => _run(emit, state, () => HiveStore.setFav(state.user, e.key, e.on)));
    on<DeletePost>((e, emit) => _run(emit, state, () => HiveStore.deletePost(e.key)));
    on<SavePost>((e, emit) => _run(emit, state, () => _save(e.key, e.post)));
  }

  Future<void> _run(Emitter<HiveState> emit, HiveState next, [Future<void> Function()? action]) async {
    emit(next.copyWith(status: Status.loading));
    try {
      await action?.call();
      final favs = HiveStore.favs(next.user);
      final keys = HiveStore.posts.keys.cast<int>().where((k) => !next.onlyFavorites || favs.contains(k)).toList();
      emit(next.copyWith(status: Status.success, favs: favs, keys: keys));
    } catch (e) {
      emit(next.copyWith(status: Status.error, error: '$e'));
    }
  }

  Future<void> _save(int? key, PostItem post) async {
    if (post.username.isEmpty || post.captionPrefix.isEmpty || post.imageAssets.isEmpty) {
      throw 'Fill username, description and pick a photo';
    }
    key == null ? await HiveStore.posts.add(post) : await HiveStore.posts.put(key, post);
  }
}
