class AppUser {
  const AppUser(this.name, this.role);

  final String name;
  final String role;

  bool get isAdmin => role == 'admin';
}
