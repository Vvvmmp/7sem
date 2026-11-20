import 'package:flutter/material.dart';

import '../data/message_db.dart';
import '../models/message.dart';
import 'message_form_screen.dart';

class MessageDetailScreen extends StatelessWidget {
  const MessageDetailScreen({super.key, required this.message});

  final Message message;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Message'),
        actions: [
          IconButton(
            icon: const Icon(Icons.edit),
            onPressed: () async {
              await Navigator.push(context, MaterialPageRoute(builder: (_) => MessageFormScreen(message: message)));
              if (context.mounted) Navigator.pop(context);
            },
          ),
          IconButton(
            icon: const Icon(Icons.delete),
            onPressed: () {
              MessageDb.delete(message.id!);
              Navigator.pop(context);
            },
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          ListTile(title: const Text('Author'), subtitle: Text(message.author)),
          ListTile(title: const Text('Content'), subtitle: Text(message.content)),
          ListTile(title: const Text('System'), subtitle: Text(message.isSystem ? 'Yes' : 'No')),
          ListTile(title: const Text('Created at'), subtitle: Text(message.createdAt.toString())),
        ],
      ),
    );
  }
}
