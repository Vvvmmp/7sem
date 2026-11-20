import 'package:messenger/mixins/activity_logger.dart';

import 'messenger_account.dart';

class WhatsappAccount extends MessengerAccount with ActivityLogger {
  WhatsappAccount(String login) : super(login);

  @override
  void sendMessage(String text) {
    print('[WhatsApp] $login: $text');
    logActivity('Send: $text');
  }
}