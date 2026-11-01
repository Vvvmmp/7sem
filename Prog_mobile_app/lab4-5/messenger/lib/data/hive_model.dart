import 'package:flutter/foundation.dart';

import 'hive_store.dart';
import 'models/post.dart';

class HiveModel extends ChangeNotifier {
  String user = 'Admin';
  bool onlyFavorites = false;

  bool get isAdmin => HiveStore.users.get(user)!.isAdmin;
  List<int> get favs => HiveStore.favs(user);
  List<int> get keys => HiveStore.posts.keys.cast<int>().where((k) => !onlyFavorites || favs.contains(k)).toList();

  void setUser(String name) {
    user = name;
    notifyListeners();
  }

  void toggleOnlyFavorites() {
    onlyFavorites = !onlyFavorites;
    notifyListeners();
  }

  Future<void> setFav(int key, bool on) async {
    await HiveStore.setFav(user, key, on);
    notifyListeners();
  }

  Future<void> deletePost(int key) async {
    await HiveStore.deletePost(key);
    notifyListeners();
  }

  Future<void> savePost(int? key, PostItem post) async {
    key == null ? await HiveStore.posts.add(post) : await HiveStore.posts.put(key, post);
    notifyListeners();
  }
}
