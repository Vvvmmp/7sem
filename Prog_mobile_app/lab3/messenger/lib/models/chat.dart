import 'dart:collection';
import 'message.dart';

class MessageIterator implements Iterator<Message> {
  final List<Message> _messages;
  int _index = -1;
  Message? _current;

  MessageIterator(this._messages);

  @override
  Message get current => _current as Message;

  @override
  bool moveNext() {
    _index++;
    if (_index >= _messages.length) {
      _current = null;
      return false;
    }
    _current = _messages[_index];
    return true;
  }
}

class Chat with IterableMixin<Message> implements Iterable<Message> {
  final List<Message> _messages;
  Chat(this._messages);
  
  @override
  Iterator<Message> get iterator => MessageIterator(_messages);
}