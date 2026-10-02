mixin ActivityLogger {
  final List<String> _log = [];

  void logActivity(String action) {
    _log.add(action);
    print('LOG: $action');
  }

  List<String> get activityLog => List.unmodifiable(_log);
}