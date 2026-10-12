import 'package:flutter/material.dart';

import '../data/message_db.dart';
import '../models/message.dart';
import 'files_screen.dart';
import 'message_detail_screen.dart';
import 'message_form_screen.dart';

class MessagesScreen extends StatefulWidget {
  const MessagesScreen({super.key});

  @override
  State<MessagesScreen> createState() => _MessagesScreenState();
}

class _MessagesScreenState extends State<MessagesScreen> {
  List<Message> _messages = [];
  String _search = '';
  String _orderBy = 'createdAt';
  bool _desc = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final messages = await MessageDb.query(search: _search, orderBy: _orderBy, desc: _desc);
    setState(() => _messages = messages);
  }

  Future<void> _open(Widget screen) async {
    await Navigator.push(context, MaterialPageRoute(builder: (_) => screen));
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Messages'),
        actions: [
          IconButton(icon: const Icon(Icons.folder_outlined), onPressed: () => _open(const FilesScreen())),
        ],
      ),
      floatingActionButton: FloatingActionButton(
        onPressed: () => _open(const MessageFormScreen()),
        child: const Icon(Icons.add),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: TextField(
              decoration: const InputDecoration(labelText: 'Search', prefixIcon: Icon(Icons.search)),
              onChanged: (value) {
                _search = value;
                _load();
              },
            ),
          ),
          Row(
            children: [
              const SizedBox(width: 16),
              const Text('Sort by: '),
              DropdownButton<String>(
                value: _orderBy,
                items: const [
                  DropdownMenuItem(value: 'createdAt', child: Text('Date')),
                  DropdownMenuItem(value: 'author', child: Text('Author')),
                  DropdownMenuItem(value: 'content', child: Text('Content')),
                  DropdownMenuItem(value: 'isSystem', child: Text('System')),
                ],
                onChanged: (value) {
                  _orderBy = value!;
                  _load();
                },
              ),
              IconButton(
                icon: Icon(_desc ? Icons.arrow_downward : Icons.arrow_upward),
                onPressed: () {
                  _desc = !_desc;
                  _load();
                },
              ),
            ],
          ),
          Expanded(
            child: ListView.builder(
              itemExtent: 72,
              itemCount: _messages.length,
              itemBuilder: (_, index) {
                final m = _messages[index];
                return ListTile(
                  leading: Icon(m.isSystem ? Icons.info_outline : Icons.person_outline),
                  title: Text(m.author),
                  subtitle: Text(m.content, maxLines: 1, overflow: TextOverflow.ellipsis),
                  onTap: () => _open(MessageDetailScreen(message: m)),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
