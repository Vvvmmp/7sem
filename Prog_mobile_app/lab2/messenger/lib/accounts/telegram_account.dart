import '../interfaces/notifiable.dart';
import 'messenger_account.dart';

class TelegramAccount extends MessengerAccount implements Notifiable {
  bool _online = true;

  TelegramAccount(String login) : super(login);
  TelegramAccount.guest() : super.guest();

  bool get online => _online;
  set online(bool value) {
    _online = value;
  }

  @override
  void sendMessage(String text) {
    if(!_online) {
      throw Exception('User $login not online');
    }
    print('[Telegram] $login: $text');
  }

  @override 
  void notify(String text) {
    print('Telegram send for $login: $text');
  }
}