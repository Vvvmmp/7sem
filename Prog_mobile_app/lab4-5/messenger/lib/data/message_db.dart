import 'package:cloud_firestore/cloud_firestore.dart';

import '../models/message.dart';

class MessageDb {
  MessageDb._();

  static final _col = FirebaseFirestore.instance.collection('messages');

  static Map<String, dynamic> _toDoc(Message m) => m.toJson()..remove('id');

  static void insert(Message m) => _col.add(_toDoc(m));

  static Future<List<Message>> query({String search = '', String orderBy = 'createdAt', bool desc = false}) async {
    final snap = await _col.orderBy(orderBy, descending: desc).get();
    return snap.docs
        .map((d) => Message.fromJson({...d.data(), 'id': d.id}))
        .where((m) => '${m.author} ${m.content}'.toLowerCase().contains(search.toLowerCase()))
        .toList();
  }

  static void update(Message m) => _col.doc(m.id).set(_toDoc(m));

  static void delete(String id) => _col.doc(id).delete();
}
