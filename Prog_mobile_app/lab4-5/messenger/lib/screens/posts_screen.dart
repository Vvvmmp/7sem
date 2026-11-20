import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/post.dart';
import '../data/posts_bloc.dart';
import '../widgets/post_card.dart';
import 'post_detail_screen.dart';
import 'post_form_screen.dart';

class PostsScreen extends StatelessWidget {
  const PostsScreen({super.key});

  void _open(BuildContext context, Widget screen) {
    Navigator.push(context, MaterialPageRoute(builder: (_) => screen));
  }

  Future<void> _openPost(BuildContext context, PostItem post) async {
    final bloc = context.read<PostsBloc>();
    final liked = await Navigator.push<bool>(
      context,
      MaterialPageRoute(builder: (_) => PostDetailScreen(post: post, liked: bloc.state.favs.contains(post.id))),
    );
    if (liked != null) bloc.add(SetFav(post.id, liked));
  }

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<PostsBloc, PostsState>(
      builder: (context, state) => Scaffold(
        appBar: AppBar(
          title: Text(state.name),
          actions: [
            IconButton(
              icon: Icon(state.onlyFavorites ? Icons.favorite : Icons.favorite_border),
              onPressed: () => context.read<PostsBloc>().add(const ToggleFavorites()),
            ),
          ],
        ),
        floatingActionButton: FloatingActionButton(
          onPressed: () => _open(context, const PostFormScreen()),
          child: const Icon(Icons.add),
        ),
        body: state.status == Status.loading
            ? const Center(child: CircularProgressIndicator())
            : ListView.builder(
                itemCount: state.visible.length,
                itemBuilder: (_, index) {
                  final post = state.visible[index];
                  final canEdit = state.isAdmin || post.ownerId == context.read<PostsBloc>().user.uid;
                  return Column(
                    children: [
                      GestureDetector(onTap: () => _openPost(context, post), child: PostCard(post: post)),
                      Row(
                        children: [
                          if (state.favs.contains(post.id))
                            const Padding(padding: EdgeInsets.only(left: 16), child: Icon(Icons.favorite)),
                          if (post.pending)
                            const Padding(padding: EdgeInsets.only(left: 16), child: Icon(Icons.cloud_upload_outlined)),
                          const Spacer(),
                          if (canEdit) ...[
                            IconButton(
                              icon: const Icon(Icons.edit),
                              onPressed: () => _open(context, PostFormScreen(post: post)),
                            ),
                            IconButton(
                              icon: const Icon(Icons.delete),
                              onPressed: () => context.read<PostsBloc>().add(DeletePost(post.id)),
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
