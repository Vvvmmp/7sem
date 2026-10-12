import 'package:sqflite/sqflite.dart';

import '../models/message.dart';

class MessageDb {
  MessageDb._();

  static Database? _db;

  static Future<Database> get _database async {
    return _db ??= await openDatabase(
      '${await getDatabasesPath()}/messages.db',
      version: 1,
      onCreate: (db, _) async {
        await db.execute(
          'CREATE TABLE messages(id INTEGER PRIMARY KEY AUTOINCREMENT, '
          'author TEXT, content TEXT, isSystem INTEGER, createdAt TEXT)',
        );
        await db.insert('messages', _toRow(Message('Nilesh', 'Hi! How are you?')));
        await db.insert('messages', _toRow(Message('Anna', 'See you tomorrow')));
        await db.insert('messages', _toRow(Message.system('Arseniy joined the chat')));
      },
    );
  }

  static Map<String, dynamic> _toRow(Message m) => {...m.toJson(), 'isSystem': m.isSystem ? 1 : 0};

  static Message _fromRow(Map<String, dynamic> row) => Message.fromJson({...row, 'isSystem': row['isSystem'] == 1});

  static Future<int> insert(Message m) async => (await _database).insert('messages', _toRow(m));

  static Future<List<Message>> query({String search = '', String orderBy = 'createdAt', bool desc = false}) async {
    final rows = await (await _database).query(
      'messages',
      where: 'author LIKE ? OR content LIKE ?',
      whereArgs: ['%$search%', '%$search%'],
      orderBy: '$orderBy ${desc ? 'DESC' : 'ASC'}',
    );
    return rows.map(_fromRow).toList();
  }

  static Future<int> update(Message m) async =>
      (await _database).update('messages', _toRow(m), where: 'id = ?', whereArgs: [m.id]);

  static Future<int> delete(int id) async => (await _database).delete('messages', where: 'id = ?', whereArgs: [id]);
}
