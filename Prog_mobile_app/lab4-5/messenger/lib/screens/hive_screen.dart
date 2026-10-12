import 'package:flutter/material.dart';

import '../data/hive_store.dart';
import '../widgets/post_card.dart';
import 'post_detail_screen.dart';
import 'post_form_screen.dart';

class HiveScreen extends StatefulWidget {
  const HiveScreen({super.key});

  @override
  State<HiveScreen> createState() => _HiveScreenState();
}

class _HiveScreenState extends State<HiveScreen> {
  String _userName = 'Admin';
  bool _onlyFavorites = false;

  bool get _isAdmin => HiveStore.users.get(_userName)!.isAdmin;

  Future<void> _open(Widget screen) async {
    await Navigator.push(context, MaterialPageRoute(builder: (_) => screen));
    setState(() {});
  }

  Future<void> _openPost(int key) async {
    final liked = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (_) => PostDetailScreen(
          post: HiveStore.posts.get(key)!,
          liked: HiveStore.favs(_userName).contains(key),
        ),
      ),
    );
    if (liked != null) await HiveStore.setFav(_userName, key, liked);
    setState(() {});
  }

  Future<void> _info(Future<String> action) async {
    final text = await action;
    if (mounted) showDialog(context: context, builder: (_) => AlertDialog(content: Text(text)));
  }

  @override
  Widget build(BuildContext context) {
    final favs = HiveStore.favs(_userName);
    final keys = HiveStore.posts.keys.cast<int>().where((k) => !_onlyFavorites || favs.contains(k)).toList();
    return Scaffold(
      appBar: AppBar(
        title: DropdownButton<String>(
          value: _userName,
          items: [
            for (final u in HiveStore.users.values)
              DropdownMenuItem(value: u.name, child: Text('${u.name} (${u.role})')),
          ],
          onChanged: (value) => setState(() => _userName = value!),
        ),
        actions: [
          IconButton(
            icon: Icon(_onlyFavorites ? Icons.favorite : Icons.favorite_border),
            onPressed: () => setState(() => _onlyFavorites = !_onlyFavorites),
          ),
          IconButton(icon: const Icon(Icons.vpn_key_outlined), onPressed: () => _info(HiveStore.wrongKeyDemo())),
          IconButton(icon: const Icon(Icons.compress), onPressed: () => _info(HiveStore.compact())),
        ],
      ),
      floatingActionButton: _isAdmin
          ? FloatingActionButton(onPressed: () => _open(const PostFormScreen()), child: const Icon(Icons.add))
          : null,
      body: ListView.builder(
        itemCount: keys.length,
        itemBuilder: (_, index) {
          final key = keys[index];
          return Column(
            children: [
              GestureDetector(onTap: () => _openPost(key), child: PostCard(post: HiveStore.posts.get(key)!)),
              Row(
                children: [
                  if (favs.contains(key)) const Padding(padding: EdgeInsets.only(left: 16), child: Icon(Icons.favorite)),
                  const Spacer(),
                  if (_isAdmin) ...[
                    IconButton(icon: const Icon(Icons.edit), onPressed: () => _open(PostFormScreen(postKey: key))),
                    IconButton(
                      icon: const Icon(Icons.delete),
                      onPressed: () async {
                        await HiveStore.posts.delete(key);
                        setState(() {});
                      },
                    ),
                  ],
                ],
              ),
            ],
          );
        },
      ),
    );
  }
}
