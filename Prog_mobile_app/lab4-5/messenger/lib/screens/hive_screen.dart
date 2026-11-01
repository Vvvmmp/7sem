import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../data/hive_model.dart';
import '../data/hive_store.dart';
import '../widgets/post_card.dart';
import 'post_detail_screen.dart';
import 'post_form_screen.dart';

class HiveScreen extends StatelessWidget {
  const HiveScreen({super.key});

  void _open(BuildContext context, Widget screen) {
    Navigator.push(context, MaterialPageRoute(builder: (_) => screen));
  }

  Future<void> _openPost(BuildContext context, int key) async {
    final model = context.read<HiveModel>();
    final liked = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (_) => PostDetailScreen(post: HiveStore.posts.get(key)!, liked: model.favs.contains(key)),
      ),
    );
    if (liked != null) await model.setFav(key, liked);
  }

  Future<void> _info(BuildContext context, Future<String> action) async {
    final text = await action;
    if (context.mounted) showDialog(context: context, builder: (_) => AlertDialog(content: Text(text)));
  }

  @override
  Widget build(BuildContext context) {
    final model = context.watch<HiveModel>();
    final favs = model.favs;
    final keys = model.keys;
    return Scaffold(
      appBar: AppBar(
        title: DropdownButton<String>(
          value: model.user,
          items: [
            for (final u in HiveStore.users.values)
              DropdownMenuItem(value: u.name, child: Text('${u.name} (${u.role})')),
          ],
          onChanged: (value) => context.read<HiveModel>().setUser(value!),
        ),
        actions: [
          IconButton(
            icon: Icon(model.onlyFavorites ? Icons.favorite : Icons.favorite_border),
            onPressed: () => context.read<HiveModel>().toggleOnlyFavorites(),
          ),
          IconButton(icon: const Icon(Icons.vpn_key_outlined), onPressed: () => _info(context, HiveStore.wrongKeyDemo())),
          IconButton(icon: const Icon(Icons.compress), onPressed: () => _info(context, HiveStore.compact())),
        ],
      ),
      floatingActionButton: model.isAdmin
          ? FloatingActionButton(onPressed: () => _open(context, const PostFormScreen()), child: const Icon(Icons.add))
          : null,
      body: ListView.builder(
        itemCount: keys.length,
        itemBuilder: (_, index) {
          final key = keys[index];
          return Column(
            children: [
              GestureDetector(onTap: () => _openPost(context, key), child: PostCard(post: HiveStore.posts.get(key)!)),
              Row(
                children: [
                  if (favs.contains(key)) const Padding(padding: EdgeInsets.only(left: 16), child: Icon(Icons.favorite)),
                  const Spacer(),
                  if (model.isAdmin) ...[
                    IconButton(icon: const Icon(Icons.edit), onPressed: () => _open(context, PostFormScreen(postKey: key))),
                    IconButton(
                      icon: const Icon(Icons.delete),
                      onPressed: () => context.read<HiveModel>().deletePost(key),
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
