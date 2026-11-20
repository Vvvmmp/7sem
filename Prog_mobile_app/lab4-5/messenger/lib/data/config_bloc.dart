import 'dart:async';

import 'package:equatable/equatable.dart';
import 'package:firebase_remote_config/firebase_remote_config.dart';
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

class ConfigChanged {}

class ConfigState extends Equatable {
  const ConfigState(this.likeEnabled, this.blockColor);

  final bool likeEnabled;
  final Color blockColor;

  @override
  List<Object?> get props => [likeEnabled, blockColor];
}

class ConfigBloc extends Bloc<ConfigChanged, ConfigState> {
  ConfigBloc() : super(_read()) {
    on<ConfigChanged>((e, emit) => emit(_read()));
    _rc.fetchAndActivate().then((_) => add(ConfigChanged()), onError: (_) {});
    _sub = _rc.onConfigUpdated.listen((_) => _rc.activate().then((_) => add(ConfigChanged())), onError: (_) {});
  }

  static final _rc = FirebaseRemoteConfig.instance;
  late final StreamSubscription _sub;

  static Future<void> init() async {
    await _rc.setConfigSettings(
      RemoteConfigSettings(fetchTimeout: const Duration(seconds: 10), minimumFetchInterval: Duration.zero),
    );
    await _rc.setDefaults({'like_enabled': true, 'block_color': 'FFFFFFFF'});
  }

  static ConfigState _read() => ConfigState(
    _rc.getBool('like_enabled'),
    Color(int.tryParse(_rc.getString('block_color'), radix: 16) ?? 0xFFFFFFFF),
  );

  @override
  Future<void> close() {
    _sub.cancel();
    return super.close();
  }
}
