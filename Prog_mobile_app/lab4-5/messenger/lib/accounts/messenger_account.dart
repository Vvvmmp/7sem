abstract class MessengerAccount {
  String login;
  static int totalAccounts = 0;

  MessengerAccount(this.login) {
    totalAccounts++;
  }

  MessengerAccount.guest() : login = 'guest_${totalAccounts + 1}' {
    totalAccounts++;
  }

  void sendMessage(String text);

  void showInfo() {
    print('Account: $login');
  }

  static void resetAccounts() {
    totalAccounts = 0;
  }
}