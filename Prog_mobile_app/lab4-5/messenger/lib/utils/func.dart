import '../accounts/messenger_account.dart';
import '../models/message.dart';
import 'dart:async';

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


Future<List<Message>> loadHistory(List<Message> local) async {
  await Future.delayed(Duration(seconds: 1));
  return local;
}

Future<String> fetchServerStatus() async {
  await Future.delayed(Duration(milliseconds: 500));
  if (DateTime.now().second % 2 == 0) {
    throw Exception('Сервер недоступен');
  }
  return 'Сервер в порядке';
}

Stream<Message> incomingMessages(List<Message> messages) async* {
  for (final msg in messages) {
    await Future.delayed(Duration(milliseconds: 300));
    yield msg;
  }
}

Stream<String> notificationBroadcast() {
  final controller = StreamController<String>.broadcast();
  var counter = 0;
  Timer.periodic(Duration(milliseconds: 500), (timer) {
    counter++;
    controller.add('Уведомление #$counter');
    if (counter >= 3) {
      timer.cancel();
      controller.close();
    }
  });
  return controller.stream;
}