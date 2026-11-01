import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/hive_bloc.dart';
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
    final bloc = context.read<HiveBloc>();
    final liked = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (_) => PostDetailScreen(post: HiveStore.posts.get(key)!, liked: bloc.state.favs.contains(key)),
      ),
    );
    if (liked != null) bloc.add(SetFav(key, liked));
  }

  Future<void> _info(BuildContext context, Future<String> action) async {
    final text = await action;
    if (context.mounted) showDialog(context: context, builder: (_) => AlertDialog(content: Text(text)));
  }

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<HiveBloc, HiveState>(
      builder: (context, state) => Scaffold(
        appBar: AppBar(
          title: DropdownButton<String>(
            value: state.user,
            items: [
              for (final u in HiveStore.users.values)
                DropdownMenuItem(value: u.name, child: Text('${u.name} (${u.role})')),
            ],
            onChanged: (value) => context.read<HiveBloc>().add(SelectUser(value!)),
          ),
          actions: [
            IconButton(
              icon: Icon(state.onlyFavorites ? Icons.favorite : Icons.favorite_border),
              onPressed: () => context.read<HiveBloc>().add(const ToggleFavorites()),
            ),
            IconButton(
              icon: const Icon(Icons.vpn_key_outlined),
              onPressed: () => _info(context, HiveStore.wrongKeyDemo()),
            ),
            IconButton(icon: const Icon(Icons.compress), onPressed: () => _info(context, HiveStore.compact())),
          ],
        ),
        floatingActionButton: state.isAdmin
            ? FloatingActionButton(
                onPressed: () => _open(context, const PostFormScreen()),
                child: const Icon(Icons.add),
              )
            : null,
        body: state.status == Status.loading
            ? const Center(child: CircularProgressIndicator())
            : ListView.builder(
                itemCount: state.keys.length,
                itemBuilder: (_, index) {
                  final key = state.keys[index];
                  return Column(
                    children: [
                      GestureDetector(
                        onTap: () => _openPost(context, key),
                        child: PostCard(post: HiveStore.posts.get(key)!),
                      ),
                      Row(
                        children: [
                          if (state.favs.contains(key))
                            const Padding(padding: EdgeInsets.only(left: 16), child: Icon(Icons.favorite)),
                          const Spacer(),
                          if (state.isAdmin) ...[
                            IconButton(
                              icon: const Icon(Icons.edit),
                              onPressed: () => _open(context, PostFormScreen(postKey: key)),
                            ),
                            IconButton(
                              icon: const Icon(Icons.delete),
                              onPressed: () => context.read<HiveBloc>().add(DeletePost(key)),
                            ),
                          ],
                        ],
                      ),
                    ],
                  );
                },
              ),
      ),
    );
  }
}
