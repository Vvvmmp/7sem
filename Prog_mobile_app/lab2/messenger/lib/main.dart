import 'interfaces/contact.dart';
import 'accounts/messenger_account.dart';
import 'accounts/telegram_account.dart';
import 'accounts/whatsapp_account.dart';
import 'models/message.dart';
import 'utils/func.dart';

void main() {
  List<Message> messages = [
    Message('Anna', 'Hi!'),
    Message.system('User joined'),
    Message('Arseniy', 'how a u?'),
    Message.system('Connection restored))'),
    Message('Anna', 'Not bad'),
  ];

  Map<String, List<Message>> chats = {
    'chat_1': messages,
  };

  Set<String> onlineUsers = {'Anna', 'Arseniy', 'Anna'}; 

  print('Online: $onlineUsers');
  print('All chats: ${chats.length}');

  int userMessagesShown = 0;
  for (var msg in messages) {
    if (msg.isSystem) {
      continue; 
    }
    if (userMessagesShown >= 2) {
      break; 
    }
    print(msg.formatted);
    userMessagesShown++;
  }

  var acc1 = TelegramAccount('user_anna');
  var acc2 = WhatsappAccount('user_arseniy');
  var guestAcc = TelegramAccount.guest();

  acc1.showInfo();
  acc2.showInfo();
  guestAcc.showInfo();
  print('All accounts: ${MessengerAccount.totalAccounts}');

  acc1.notify('you have a new message');

  acc1.online = false;
  try {
    acc1.sendMessage('Hello');
  } catch (e) {
    print('Catch error: $e');
  } finally {
    print('Try to send');
  }
  acc1.online = true;

  sendBulkMessages(
    [acc1, acc2],
    'Send news',
    onSent: (login) => print('Delivered: $login'),
    repeat: 2,
  );

  print(buildGreeting('Marya'));
  print(buildGreeting('Adam', 'Hello'));

  Contact c = VerifiedContact('Marya');
  print(c.getDisplayName());

  var m = Message('Adam', 'first try');
  try {
    m.content = '';
  } catch (e) {
    print('Error for establish content: $e');
  } finally {
    print('Current content: ${m.content}');
  }
}