import 'package:flutter/material.dart';

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
  late String _image = _old?.imageAssets.first ?? _photos.first;

  Future<void> _save() async {
    if (_username.text.isEmpty || _caption.text.isEmpty) return;
    final post = PostItem(
      avatarAsset: _old?.avatarAsset ?? 'assets/images/avatar_me.jpg',
      username: _username.text,
      meta: _meta.text,
      captionPrefix: _caption.text,
      captionMention: _old?.captionMention ?? '',
      captionSuffix: _old?.captionSuffix ?? '',
      imageAssets: [_image],
    );
    widget.postKey == null ? await HiveStore.posts.add(post) : await HiveStore.posts.put(widget.postKey, post);
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
          DropdownButton<String>(
            value: _image,
            isExpanded: true,
            items: [for (final p in _photos) DropdownMenuItem(value: p, child: Text(p))],
            onChanged: (value) => setState(() => _image = value!),
          ),
          ElevatedButton(onPressed: _save, child: const Text('Save')),
        ],
      ),
    );
  }
}
