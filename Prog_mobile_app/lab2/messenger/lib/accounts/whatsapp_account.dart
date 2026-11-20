import 'messenger_account.dart';

class WhatsappAccount extends MessengerAccount {
  WhatsappAccount(String login) : super(login);

  @override
  void sendMessage(String text) {
    print('[WhatsApp] $login: $text');
  }
}