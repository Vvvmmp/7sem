import 'dart:convert';
import 'dart:io';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:hive/hive.dart';

import 'hive_adapters.dart';
import 'models/app_user.dart';
import 'models/post.dart';
import 'sample_data.dart';

class HiveStore {
  HiveStore._();

  static late Box<AppUser> users;
  static late Box<PostItem> posts;
  static late Box favorites; 

  static Future<List<int>> secureKey() async {
    const storage = FlutterSecureStorage();
    final saved = await storage.read(key: 'hive_key');
    if (saved != null) return base64Decode(saved);
    final key = Hive.generateSecureKey();
    await storage.write(key: 'hive_key', value: base64Encode(key));
    return key;
  }

  static Future<void> init(String dir, List<int> key) async {
    Hive
      ..init(dir)
      ..registerAdapter(AppUserAdapter())
      ..registerAdapter(PostItemAdapter());
    final cipher = HiveAesCipher(key);
    users = await Hive.openBox<AppUser>('users', encryptionCipher: cipher);
    posts = await Hive.openBox<PostItem>('posts', encryptionCipher: cipher);
    favorites = await Hive.openBox('favorites', encryptionCipher: cipher);
    if (users.isEmpty) {
      await users.put('Admin', const AppUser('Admin', 'admin'));
      await users.put('Anna', const AppUser('Anna', 'user'));
      await posts.addAll(SampleData.feedPosts);
    }
  }

  static List<int> favs(String user) => List<int>.from(favorites.get(user, defaultValue: <int>[]));

  static Future<void> setFav(String user, int postKey, bool on) {
    final list = favs(user)..remove(postKey);
    if (on) list.add(postKey);
    return favorites.put(user, list);
  }

  static Future<void> deletePost(int postKey) async {
    await posts.delete(postKey);
    for (final user in favorites.keys) {
      await setFav(user, postKey, false);
    }
  }

  static Future<String> wrongKeyDemo() async {
    final dir = File(posts.path!).parent.path;
    await File(posts.path!).copy('$dir/posts_demo.hive');
    try {
      final box = await Hive.openBox<PostItem>(
        'posts_demo',
        encryptionCipher: HiveAesCipher(Hive.generateSecureKey()),
        crashRecovery: false,
      );
      return 'Right key: ${posts.length} posts\nWrong key: opened, ${box.length} posts';
    } catch (e) {
      return 'Right key: ${posts.length} posts\nWrong key: error\n$e';
    } finally {
      await Hive.deleteBoxFromDisk('posts_demo');
    }
  }

  static Future<String> compact() async {
    final file = File(posts.path!);
    final before = file.lengthSync();
    await posts.compact();
    return 'Box size: $before -> ${file.lengthSync()} bytes';
  }
}
