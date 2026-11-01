import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../data/hive_model.dart';
import '../data/hive_store.dart';
import '../data/models/post.dart';

final _photos = [for (var i = 1; i <= 5; i++) 'assets/images/photo_$i.jpg'];

class PostFormScreen extends StatefulWidget {
  const PostFormScreen({super.key, this.postKey});

  final int? postKey;

  @override
  State<PostFormScreen> createState() => _PostFormScreenState();
}

class _PostFormScreenState extends State<PostFormScreen> {
  late final PostItem? _old = HiveStore.posts.get(widget.postKey);
  late final _username = TextEditingController(text: _old?.username);
  late final _meta = TextEditingController(text: _old?.meta);
  late final _caption = TextEditingController(text: _old?.captionPrefix);
  late final List<String> _images = [...?_old?.imageAssets];

  Future<void> _save() async {
    if (_username.text.isEmpty || _caption.text.isEmpty || _images.isEmpty) return;
    final post = PostItem(
      avatarAsset: _old?.avatarAsset ?? 'assets/images/avatar_me.jpg',
      username: _username.text,
      meta: _meta.text,
      captionPrefix: _caption.text,
      captionMention: _old?.captionMention ?? '',
      captionSuffix: _old?.captionSuffix ?? '',
      imageAssets: _images,
    );
    await context.read<HiveModel>().savePost(widget.postKey, post);
    if (mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.postKey == null ? 'New post' : 'Edit post')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          TextField(controller: _username, decoration: const InputDecoration(labelText: 'Username')),
          TextField(controller: _meta, decoration: const InputDecoration(labelText: 'Location')),
          TextField(controller: _caption, decoration: const InputDecoration(labelText: 'Description')),
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
    );
  }
}
