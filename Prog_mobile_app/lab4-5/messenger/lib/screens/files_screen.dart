import 'dart:convert';
import 'dart:io';
import 'dart:isolate';

import 'package:flutter/material.dart';
import 'package:path_provider/path_provider.dart';

import '../models/message.dart';

final _directories = <(String, Future<Directory?> Function(), String)>[
  ('Temporary', getTemporaryDirectory, '-'),
  ('Application Support', getApplicationSupportDirectory, '-'),
  ('Application Library', getLibraryDirectory, 'Android, Windows, Linux'),
  ('Application Documents', getApplicationDocumentsDirectory, '-'),
  ('Application Cache', getApplicationCacheDirectory, '-'),
  ('External Storage', getExternalStorageDirectory, 'iOS, macOS, Windows, Linux'),
  ('External Cache Directories', () async => (await getExternalCacheDirectories())?.first, 'iOS, macOS, Windows, Linux'),
  ('External Storage Directories', () async => (await getExternalStorageDirectories())?.first, 'iOS, macOS, Windows, Linux'),
  ('Downloads', getDownloadsDirectory, '-'),
];

Future<String?> _writeAndReadInIsolate(String path, String? json) => Isolate.run(() {
  final file = File(path);
  if (json != null) {
    file.parent.createSync(recursive: true); 
    file.writeAsStringSync(json);
  }
  return file.existsSync() ? file.readAsStringSync() : null;
});

class FilesScreen extends StatefulWidget {
  const FilesScreen({super.key});

  @override
  State<FilesScreen> createState() => _FilesScreenState();
}

class _FilesScreenState extends State<FilesScreen> {
  final List<String> _results = [];

  Future<void> _run({required bool write}) async {
    final results = <String>[];
    for (final (name, getDir, unsupported) in _directories) {
      try {
        final dir = await getDir();
        if (dir == null) throw UnsupportedError('directory is null');
        final path = '${dir.path}/${name.toLowerCase().replaceAll(' ', '_')}.json';
        final json = write ? jsonEncode(Message('Files', 'Saved to $name').toJson()) : null;
        final content = await _writeAndReadInIsolate(path, json);
        if (content == null) {
          results.add('$name\n$path\nfile not found');
        } else {
          final message = Message.fromJson(jsonDecode(content));
          results.add('$name\n$path\n${message.formatted} (${message.createdAt})');
        }
      } on UnsupportedError {
        results.add('$name\nERROR: not supported on ${Platform.operatingSystem}. Unsupported platforms: $unsupported');
      } catch (e) {
        results.add('$name\nERROR: $e');
      }
    }
    setState(() => _results
      ..clear()
      ..addAll(results));
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Files')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          ElevatedButton(onPressed: () => _run(write: true), child: const Text('Write & read files')),
          ElevatedButton(onPressed: () => _run(write: false), child: const Text('Show existing files')),
          for (final result in _results) Card(child: Padding(padding: const EdgeInsets.all(12), child: Text(result))),
        ],
      ),
    );
  }
}
