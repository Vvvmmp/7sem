class Message {
  String _content;
  final String author;
  final bool isSystem;

  Message(this.author, this._content) : isSystem = false;

  Message.system(this._content) : author = 'system', isSystem = true;

  String get content => _content;
  set content(String value) {
    if (value.isEmpty) {
      throw ArgumentError('Message can not be empty');
    }
    _content = value;
  }

  String get formatted => '$author: $_content';
}