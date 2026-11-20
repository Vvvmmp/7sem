class Contact {
  final String name;
  Contact(this.name);

  String getDisplayName() => name;
}

class VerifiedContact implements Contact {
  @override
  final String name;
  VerifiedContact(this.name);
  
  @override
  String getDisplayName() => '$name (!)';
}