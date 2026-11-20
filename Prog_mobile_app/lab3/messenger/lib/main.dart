import 'interfaces/contact.dart';
import 'accounts/messenger_account.dart';
import 'accounts/telegram_account.dart';
import 'accounts/whatsapp_account.dart';
import 'models/message.dart';
import 'utils/func.dart';
import 'models/chat.dart';
import 'dart:async';
import 'dart:convert';

void main() async {
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

  final chat = Chat(messages);
for (final m in chat) {
  print('[Chat] ${m.formatted}');
}

final sortedByTime = [...messages]..sort(); 
print('Отсортировано по времени: ${sortedByTime.map((m) => m.content)}');


final jsonString = jsonEncode(messages.first.toJson());
print('JSON: $jsonString');

final restored = Message.fromJson(jsonDecode(jsonString));
print('Восстановлено: ${restored.formatted}');

await demoFuture(messages);
}


Future<void> demoFuture(List<Message> messages) async {
  try {
    final status = await fetchServerStatus();
    print('Статус: $status');
  } catch (e) {
    print('Ошибка: $e');
  }

  fetchServerStatus()
      .then((status) => print('[then] $status'))
      .catchError((e) => print('[catchError] $e'))
      .whenComplete(() => print('[whenComplete] запрос завершён'));

  final history = await loadHistory(messages);
  print('Загружено сообщений: ${history.length}');

  await demoStreams(messages);
}

Future<void> demoStreams(List<Message> messages) async {
  final singleDone = Completer<void>();
  var receivedCount = 0;
  late final StreamSubscription<String> singleSub;
  singleSub = incomingMessages(messages)
      .where((msg) => !msg.isSystem)
      .map((msg) => msg.formatted.toUpperCase())
      .listen(
        (formatted) {
          print('[Stream] $formatted');
          receivedCount++;
          if (receivedCount >= 2) {
            print('[Stream] Отменяем подписку после 2 сообщений');
            singleSub.cancel();
            if (!singleDone.isCompleted) singleDone.complete();
          }
        },
        onDone: () {
          if (!singleDone.isCompleted) singleDone.complete();
        },
      );
  await singleDone.future;

  print('Broadcast stream');
  final broadcast = notificationBroadcast();
  final firstDone = Completer<void>();
  final secondDone = Completer<void>();

  broadcast.listen(
    (note) => print('[Broadcast:1] $note'),
    onDone: () => firstDone.complete(),
  );

  var secondCount = 0;
  late final StreamSubscription<String> secondSub;
  secondSub = broadcast
      .transform(
        StreamTransformer<String, String>.fromHandlers(
          handleData: (data, sink) => sink.add('!!! $data'),
        ),
      )
      .listen((note) {
    print('[Broadcast:2] $note');
    secondCount++;
    if (secondCount >= 1) {
      print('[Broadcast:2] Отменяем подписку после первого уведомления');
      secondSub.cancel();
      secondDone.complete();
    }
  });

  await Future.wait([firstDone.future, secondDone.future]);
}