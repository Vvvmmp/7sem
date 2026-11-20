class Message implements Comparable<Message> {
  final String? id;
  String _content;
  final String author;
  final bool isSystem;
  final DateTime createdAt;

  Message(this.author, this._content, {this.id, DateTime? createdAt}) : isSystem = false,
        createdAt = createdAt ?? DateTime.now();

  Message.system(this._content, {this.id, DateTime? createdAt}) : author = 'system', isSystem = true,
        createdAt = createdAt ?? DateTime.now();

  String get content => _content;
  set content(String value) {
    if (value.isEmpty) {
      throw ArgumentError('Message can not be empty');
    }
    _content = value;
  }

  String get formatted => '$author: $_content';

  @override
  int compareTo(Message other) => createdAt.compareTo(other.createdAt);

  @override
  String toString() => formatted;

    Map<String, dynamic> toJson() => {
        'id': id,
        'author': author,
        'content': _content,
        'isSystem': isSystem,
        'createdAt': createdAt.toIso8601String(),
      };

  factory Message.fromJson(Map<String, dynamic> json) {
    final createdAt = DateTime.parse(json['createdAt'] as String);
    final id = json['id'] as String?;
    if (json['isSystem'] == true) {
      return Message.system(json['content'] as String, id: id, createdAt: createdAt);
    }
    return Message(json['author'] as String, json['content'] as String,
        id: id, createdAt: createdAt);
  }
}