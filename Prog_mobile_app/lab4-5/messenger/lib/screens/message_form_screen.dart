import 'package:flutter/material.dart';

import '../data/message_db.dart';
import '../models/message.dart';

class MessageFormScreen extends StatefulWidget {
  const MessageFormScreen({super.key, this.message});

  final Message? message;

  @override
  State<MessageFormScreen> createState() => _MessageFormScreenState();
}

class _MessageFormScreenState extends State<MessageFormScreen> {
  late final _author = TextEditingController(text: widget.message?.author);
  late final _content = TextEditingController(text: widget.message?.content);
  late bool _isSystem = widget.message?.isSystem ?? false;

  @override
  void dispose() {
    _author.dispose();
    _content.dispose();
    super.dispose();
  }

  void _save() {
    if (_content.text.isEmpty || (!_isSystem && _author.text.isEmpty)) return;
    final old = widget.message;
    final message = _isSystem
        ? Message.system(_content.text, id: old?.id, createdAt: old?.createdAt)
        : Message(_author.text, _content.text, id: old?.id, createdAt: old?.createdAt);
    old == null ? MessageDb.insert(message) : MessageDb.update(message);
    if (mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.message == null ? 'New message' : 'Edit message')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          TextField(
            controller: _author,
            enabled: !_isSystem,
            decoration: const InputDecoration(labelText: 'Author'),
          ),
          TextField(controller: _content, decoration: const InputDecoration(labelText: 'Content')),
          SwitchListTile(
            title: const Text('System message'),
            value: _isSystem,
            onChanged: (value) => setState(() => _isSystem = value),
          ),
          ElevatedButton(onPressed: _save, child: const Text('Save')),
        ],
      ),
    );
  }
}
