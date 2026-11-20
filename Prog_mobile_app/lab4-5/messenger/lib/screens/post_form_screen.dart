import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/post.dart';
import '../data/posts_bloc.dart';

final _photos = [for (var i = 1; i <= 5; i++) 'assets/images/photo_$i.jpg'];

class PostFormScreen extends StatefulWidget {
  const PostFormScreen({super.key, this.post});

  final PostItem? post;

  @override
  State<PostFormScreen> createState() => _PostFormScreenState();
}

class _PostFormScreenState extends State<PostFormScreen> {
  late final PostItem? _old = widget.post;
  late final _username = TextEditingController(text: _old?.username);
  late final _meta = TextEditingController(text: _old?.meta);
  late final _caption = TextEditingController(text: _old?.captionPrefix);
  late final List<String> _images = [...?_old?.imageAssets];

  void _save() {
    final post = PostItem(
      id: _old?.id ?? '',
      avatarAsset: _old?.avatarAsset ?? 'assets/images/avatar_me.jpg',
      username: _username.text,
      meta: _meta.text,
      captionPrefix: _caption.text,
      captionMention: _old?.captionMention ?? '',
      captionSuffix: _old?.captionSuffix ?? '',
      imageAssets: _images,
    );
    context.read<PostsBloc>().add(SavePost(post));
  }

  @override
  Widget build(BuildContext context) {
    return BlocListener<PostsBloc, PostsState>(
      listenWhen: (prev, curr) => prev.status == Status.loading && curr.status != Status.loading,
      listener: (context, state) => state.status == Status.error
          ? ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(state.error)))
          : Navigator.pop(context),
      child: Scaffold(
        appBar: AppBar(title: Text(widget.post == null ? 'New post' : 'Edit post')),
        body: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            TextField(
              controller: _username,
              decoration: const InputDecoration(labelText: 'Username'),
            ),
            TextField(
              controller: _meta,
              decoration: const InputDecoration(labelText: 'Location'),
            ),
            TextField(
              controller: _caption,
              decoration: const InputDecoration(labelText: 'Description'),
            ),
            Wrap(
              spacing: 8,
              children: [
                for (final p in {..._photos, ..._images})
                  FilterChip(
                    label: Text(p.split('/').last),
                    selected: _images.contains(p),
                    onSelected: (on) => setState(() => on ? _images.add(p) : _images.remove(p)),
                  ),
              ],
            ),
            ElevatedButton(onPressed: _save, child: const Text('Save')),
          ],
        ),
      ),
    );
  }
}
