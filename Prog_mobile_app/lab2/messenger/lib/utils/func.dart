import '../accounts/messenger_account.dart';

void sendBulkMessages(
  List<MessengerAccount> accounts,
  String text, {
    String prefix = 'Important: ',
    void Function(String login)? onSent,
    int repeat = 1,
  }
) {
  for (var account in accounts) {
    for (var i = 0; i < repeat; i++) {
      try {
        account.sendMessage('$prefix$text');
        onSent?.call(account.login);
      } catch (e) {
        print('Error send for ${account.login}: $e');
      }
    }
  }
}

String buildGreeting(String name, [String greeting = 'Hello']) {
  return '$greeting, $name!';
}