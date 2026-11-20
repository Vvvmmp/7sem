# Лабораторная №11-12 — Firebase

Основа — приложение из лабы №10 (BLoC). Hive и SQLite убраны, все данные теперь в Firebase. Экран Hive переименован в экран постов (`PostsScreen`), `HiveBloc` — в `PostsBloc`.

## Настройка Firebase (один раз)

1. В [Firebase Console](https://console.firebase.google.com) создать проект и добавить Android-приложение с пакетом `com.example.messenger`.
2. Включить: Firestore, Realtime Database, Authentication (Email/Password и GitHub), Remote Config, Analytics.
3. Скачать `google-services.json` в `android/app/` (после создания Realtime Database, чтобы в файле был её URL).
4. GitHub: создать OAuth App на GitHub (Settings → Developer settings). Callback URL взять из Firebase Console → Authentication → GitHub. Client ID и Secret вставить туда же.
5. Remote Config: создать параметры `like_enabled` (Boolean, `true`) и `block_color` (String, `FFFFFFFF`).
6. Правила: содержимое [firestore.rules](firestore.rules) вставить в Firestore → Rules, [database.rules.json](database.rules.json) — в Realtime Database → Rules.
7. `flutter run` на Android-устройстве или эмуляторе с Google Play.

## Задание 1

**Условие:** Доработать приложение лабораторной 10 (с сохранением архитектуры BLoC) для хранения всех сущностей в Firebase Firestore.

**Где в коде:**
- BLoC: [posts_bloc.dart](lib/data/posts_bloc.dart), создаётся в [main.dart:47-51](lib/main.dart#L47-L51)
- Посты: коллекция `posts`, [posts_bloc.dart:137](lib/data/posts_bloc.dart#L137); модель с `fromDoc`/`toMap`: [post.dart:17](lib/data/models/post.dart#L17), [post.dart:44](lib/data/models/post.dart#L44)
- Пользователи и лайки: коллекция `users`, [posts_bloc.dart:138](lib/data/posts_bloc.dart#L138)
- Сообщения (были в SQLite): коллекция `messages`, [message_db.dart](lib/data/message_db.dart)

**Как работает:** Архитектура та же: экраны отправляют события (`SavePost`, `DeletePost`, `SetFav`…), BLoC меняет данные и выдаёт состояние. Изменилось только хранилище: вместо боксов Hive — коллекции Firestore. Документ `users/{uid}` хранит имя, email, роль и список лайкнутых постов. BLoC подписан на коллекцию `posts` и на документ пользователя, поэтому любое изменение в базе сразу попадает в UI.

## Задание 2

**Условие:** Реализовать CRUD операции для сущностей.

**Где в коде:**
- Create / Update: [posts_bloc.dart:122-133](lib/data/posts_bloc.dart#L122-L133), форма [post_form_screen.dart:36](lib/screens/post_form_screen.dart#L36)
- Read: [posts_bloc.dart:100-104](lib/data/posts_bloc.dart#L100-L104)
- Delete: [posts_bloc.dart:117-121](lib/data/posts_bloc.dart#L117-L121), кнопка [posts_screen.dart:67](lib/screens/posts_screen.dart#L67)
- Лайки (update документа пользователя): [posts_bloc.dart:113-116](lib/data/posts_bloc.dart#L113-L116)
- Сообщения: [message_db.dart:12-24](lib/data/message_db.dart#L12-L24)

**Как работает:** Лента → иконка-хранилище → экран постов. `+` создаёт пост (`add`), карандаш — редактирует (`update`), корзина — удаляет (`delete`). Список читается подпиской `snapshots()`, поэтому после любой операции обновляется сам. Править и удалять можно только свои посты (или все, если роль `admin`). Сообщения (иконка-чат в ленте): то же самое, но через `get()` при открытии экрана.

## Задание 3

**Условие:** Настроить и продемонстрировать работу приложения без доступа к интернету и последующую синхронизацию данных.

**Где в коде:**
- Кэш на диске: [main.dart:23](lib/main.dart#L23)
- Подписка с метаданными: [posts_bloc.dart:101](lib/data/posts_bloc.dart#L101), флаг `pending`: [post.dart:22](lib/data/models/post.dart#L22)
- Иконка «ждёт отправки»: [posts_screen.dart:57-58](lib/screens/posts_screen.dart#L57-L58)
- Запись без `await`: [posts_bloc.dart:128-130](lib/data/posts_bloc.dart#L128-L130)

**Как работает:** Firestore хранит копию данных на телефоне. Без сети чтение идёт из этой копии, а записи сразу применяются к ней и встают в очередь. Мы не ждём ответа сервера (`await` нет), иначе без сети форма бы зависла. У ещё не отправленных постов `hasPendingWrites = true`, рядом с ними видна иконка облака. Как проверить: включить режим полёта → создать/изменить/удалить пост (всё работает, у поста иконка облака) → выключить режим полёта → иконка исчезает, а в Firebase Console появляются изменения.

## Задание 4

**Условие:** Реализовать регистрацию и аутентификацию (email/password провайдер, Firebase Auth).

**Где в коде:**
- Экран входа: [login_screen.dart](lib/screens/login_screen.dart)
- Вход: [login_screen.dart:43-50](lib/screens/login_screen.dart#L43-L50), регистрация: [login_screen.dart:51-58](lib/screens/login_screen.dart#L51-L58)
- Переключение экранов по состоянию входа: [main.dart:42-57](lib/main.dart#L42-L57)
- Выход: [presence.dart:35-39](lib/data/presence.dart#L35-L39)

**Как работает:** `createUserWithEmailAndPassword` создаёт аккаунт, `signInWithEmailAndPassword` входит. Приложение слушает `authStateChanges()`: нет пользователя — показывается `LoginScreen`, есть — лента. Для каждого пользователя создаётся свой `PostsBloc` (`key: uid`), поэтому после выхода и входа другим аккаунтом данные не смешиваются. Ошибки Firebase (неверный пароль, занятый email) показываются снекбаром.

## Задание 5

**Условие:** Добавить кнопку сброса пароля, отправляющую ссылку на email.

**Где в коде:**
- [login_screen.dart:59-66](lib/screens/login_screen.dart#L59-L66)

**Как работает:** Ввести email и нажать «Forgot password?». `sendPasswordResetEmail` просит Firebase отправить письмо со ссылкой. По ссылке открывается страница Firebase, где задаётся новый пароль.

## Задание 6

**Условие:** Использовать дополнительный провайдер для аутентификации (Google, GitHub, Microsoft и т. д.).

**Где в коде:**
- GitHub: [login_screen.dart:68-75](lib/screens/login_screen.dart#L68-L75)

**Как работает:** `signInWithProvider(GithubAuthProvider())` открывает в браузере страницу входа GitHub. После подтверждения GitHub возвращает Firebase токен, и пользователь входит в приложение. Отдельный пакет не нужен: это умеет сам `firebase_auth`.

## Задание 7

**Условие:** Добавить страницу с информацией о текущем пользователе.

**Где в коде:**
- [account_screen.dart](lib/screens/account_screen.dart), кнопка-человечек в шапке ленты: [feed_screen.dart:64](lib/screens/feed_screen.dart#L64)

**Как работает:** Берётся `FirebaseAuth.instance.currentUser` и выводятся UID, email, имя, провайдеры входа, подтверждён ли email, даты регистрации и последнего входа. Роль приходит из Firestore через BLoC, статус — из Realtime Database (задание 8). Внизу кнопка выхода.

## Задание 8

**Условие:** Добавить хранение статуса пользователя (online/offline и время последней активности) в Realtime Database.

**Где в коде:**
- [presence.dart](lib/data/presence.dart): запуск [main.dart:31](lib/main.dart#L31)
- Online при подключении + offline при обрыве: [presence.dart:27-31](lib/data/presence.dart#L27-L31)
- Сворачивание/разворачивание приложения: [presence.dart:23](lib/data/presence.dart#L23)
- Показ: [account_screen.dart:32-42](lib/screens/account_screen.dart#L32-L42)
- Правила: [database.rules.json](database.rules.json)

**Как работает:** В базе лежит `status/{uid} = {online, lastSeen}`. Специальный путь `.info/connected` сообщает, есть ли связь с сервером. Когда связь появляется, приложение пишет `online: true` и заранее регистрирует `onDisconnect()`. Это запись, которую выполнит **сервер**, когда соединение пропадёт (убили приложение, пропала сеть). При сворачивании приложения и при выходе пишется `online: false`. `lastSeen` — серверное время (`ServerValue.timestamp`) последнего изменения статуса.

## Задание 9

**Условие:** Реализовать и продемонстрировать Firebase Messaging с отображением на телефоне.

**Где в коде:**
- Запрос разрешения и показ в открытом приложении: [main.dart:25-30](lib/main.dart#L25-L30)

**Как работает:** При запуске запрашивается разрешение на уведомления (на Android 13+ появится системный диалог). Отправка: Firebase Console → Messaging → New campaign → Notifications, в Target выбрать приложение. Если приложение свёрнуто или закрыто, Android сам показывает системное уведомление в шторке. Если приложение открыто, система уведомление не показывает. Тогда его ловит `onMessage`, и заголовок с текстом выводятся снекбаром внизу экрана.

## Задание 10

**Условие:** Использовать Firebase Remote Config для управления функционалом приложения (например, отключение/включение кнопки like).

**Где в коде:**
- BLoC конфига: [config_bloc.dart](lib/data/config_bloc.dart)
- Значения по умолчанию и интервал загрузки: [config_bloc.dart:30-35](lib/data/config_bloc.dart#L30-L35)
- Загрузка и обновление в реальном времени: [config_bloc.dart:23-24](lib/data/config_bloc.dart#L23-L24)
- Кнопка Like: [post_detail_screen.dart:81-83](lib/screens/post_detail_screen.dart#L81-L83)

**Как работает:** При старте `fetchAndActivate()` скачивает параметры. `onConfigUpdated` получает изменения сразу после публикации в консоли, без перезапуска. После этого `ConfigBloc` выдаёт новое состояние. Если `like_enabled = false`, у кнопки Like `onPressed: null`, и она становится серой и неактивной. Без сети используются последние скачанные значения или значения по умолчанию.

## Задание 11

**Условие:** Используя Firebase Remote Config изменить цвет блока в приложении.

**Где в коде:**
- Чтение цвета: [config_bloc.dart:37-40](lib/data/config_bloc.dart#L37-L40)
- Блок «Recently Post» в ленте: [feed_screen.dart:130](lib/screens/feed_screen.dart#L130)

**Как работает:** Параметр `block_color` — строка с цветом в формате `AARRGGBB` (например, `FFFFE0E0` — розовый). Она превращается в `Color`, и этим цветом заливается белая карточка со списком постов в ленте. Если изменить значение в консоли и нажать Publish, цвет поменяется прямо в открытом приложении.

## Задание 12

**Условие:** Добавить в приложение аналитику (Analytics) минимум для 5 событий.

**Где в коде:**
- `login` (password / github): [login_screen.dart:47](lib/screens/login_screen.dart#L47), [login_screen.dart:71](lib/screens/login_screen.dart#L71)
- `sign_up`: [login_screen.dart:55](lib/screens/login_screen.dart#L55)
- `password_reset`: [login_screen.dart:63](lib/screens/login_screen.dart#L63)
- `like_post`: [posts_bloc.dart:115](lib/data/posts_bloc.dart#L115)
- `delete_post`: [posts_bloc.dart:120](lib/data/posts_bloc.dart#L120)
- `create_post` / `edit_post`: [posts_bloc.dart:131](lib/data/posts_bloc.dart#L131)
- `logout`: [presence.dart:37](lib/data/presence.dart#L37)

**Как работает:** `FirebaseAnalytics.instance.logEvent(name: ...)` отправляет событие, при желании с параметрами (например, `post_id`). Обычно события появляются в отчётах с задержкой до суток. Чтобы видеть их сразу, включить режим отладки `adb shell setprop debug.firebase.analytics.app com.example.messenger` и открыть Analytics → DebugView.

## Задание 13

**Условие:** Настроить Firestore Security Rules. Добавить проверки на пользователя, ограничения на поля, запретить изменения некоторых полей.

**Где в коде:**
- [firestore.rules](firestore.rules)

**Как работает:**
- **Проверки на пользователя:** без входа ничего читать и писать нельзя (`signedIn()`). Документ `users/{uid}` может создать и менять только его владелец. Пост можно создать только от своего имени (`ownerId == request.auth.uid`), а менять и удалять — только автору или админу (`isAdmin()` читает роль из `users`).
- **Ограничения на поля:** `validPost` пропускает только известные поля; `username` — строка 1–30 символов, описание — 1–500, фото — от 1 до 5. `createdAt` должен быть равен серверному времени запроса. У пользователя при создании роль только `user`. У сообщения текст не пустой.
- **Запрет изменения полей:** у поста нельзя менять `ownerId` и `createdAt`. У пользователя можно менять только `name` и `favs`, поэтому роль `admin` себе не выдать. Назначить админа можно только вручную в консоли.

Проверить можно в Firestore → Rules → Rules Playground, или в приложении: запрещённая запись откатывается, когда сервер её отклоняет.


---

# Лабораторная №13 — Unit, Widget и Integration тесты

Тестируется приложение из лабы №11-12. Единственное изменение в коде приложения: `PostsBloc` принимает необязательный второй аргумент — `FirebaseFirestore` ([posts_bloc.dart](lib/data/posts_bloc.dart)), чтобы в тестах подставлять мок. В самом приложении ничего не меняется.

Запуск: `flutter test` (unit + widget). Моки генерируются командой `dart run build_runner build` (файл `test/mocks.mocks.dart` уже сгенерирован).

## Задание 1

**Условие:** Минимум 5 Unit-тестов с использованием Mockito.

**Где в коде:**
- [test/unit_test.dart](test/unit_test.dart) — 6 тестов
- [test/mocks.dart](test/mocks.dart) — список классов для мокирования (`@GenerateNiceMocks`) и вспомогательные функции

**Как работает:** Тесты проверяют логику без Firebase и без UI. Firestore, документы и пользователь заменены моками Mockito: `when(...).thenReturn(...)` задаёт, что вернёт мок, `verify(...)` проверяет, что мок вызвали.
1. `PostsState.visible` — при включённом фильтре остаются только избранные посты.
2. `PostItem.fromDoc` — документ Firestore (мок) превращается в `PostItem`.
3. `WatchUser` — из мок-документа `users/{uid}` читаются имя, роль admin и избранное.
4. `SavePost` с пустыми полями — состояние `error`, запись в Firestore не вызывается (`verifyNever`).
5. `SetFav` — обновляется документ пользователя (`verify` вызова `update`).
6. `ToggleFavorites` — флаг `onlyFavorites` переключается.

## Задание 2

**Условие:** Минимум 5 Widget-тестов, покрывающих `enterText`, `tap`, `drag`.

**Где в коде:**
- [test/widget_test.dart](test/widget_test.dart) — 6 тестов
- `enterText`: тест формы, [строки с `t.enterText`](test/widget_test.dart)
- `tap`: тесты формы, кнопки Like, сердечка
- `drag`: `dragUntilVisible` в тестах списка постов и экрана поста

**Как работает:** Экран строится в тестовой среде (`pumpWidget`), `PostsBloc` и `ConfigBloc` — моки Mockito с заданным состоянием. Тест «действует» как пользователь и смотрит на результат:
1. Форма: `enterText` в поля Username/Description + `tap` по фото и «Save» → в блок ушло событие `SavePost` с введёнными значениями.
2. Форма: `tap` по чипу фото переключает его выбор.
3. Экран поста: `drag` до кнопки и `tap` по Like → надпись становится «Liked».
4. Экран поста: если Remote Config отключил лайк, кнопка неактивна.
5. Список постов: `drag` вниз докручивает до 12-го поста.
6. Список постов: `tap` по сердечку отправляет `ToggleFavorites`.

## Задание 3

**Условие:** Три integration-теста: от админа с добавлением, от пользователя с «избранным», с информацией о текущем пользователе.

**Где в коде:** [integration_test/app_test.dart](integration_test/app_test.dart)

**Как работает:** Тесты запускают настоящее приложение на эмуляторе/телефоне с реальным Firebase, входят через экран логина и проходят сценарий кликами. Нужны два заранее созданных аккаунта (админ — с `role: admin` в `users/{uid}`, и обычный пользователь), данные передаются через `--dart-define`:

```
flutter test integration_test -d <device> \
  --dart-define=ADMIN_EMAIL=... --dart-define=ADMIN_PASS=... \
  --dart-define=USER_EMAIL=... --dart-define=USER_PASS=...
```
1. Админ: вход → экран постов → добавить пост → пост появился в списке → удалить его.
2. Пользователь: вход → создать пост → открыть, нажать Like → вернуться → включить фильтр «избранное» → пост виден, сердечко отмечено.
3. Информация о пользователе: вход → экран Account → видны email, UID и провайдер `password`.

## Вопросы

1. **Unit-тест** — проверяет одну маленькую единицу кода (функцию, класс, BLoC) изолированно, без UI и сети. Быстрый, нужен чтобы ловить ошибки в логике и безопасно рефакторить.
2. **Widget-тест** — собирает один виджет/экран в тестовой среде Flutter, без эмулятора, и имитирует действия пользователя (`tap`, `enterText`, `drag`). Проверяет, что UI правильно показывает состояние и реагирует на ввод.
3. **Integration-тест** — запускает всё приложение целиком на устройстве/эмуляторе с реальными сервисами и проходит пользовательский сценарий. Проверяет, что части работают вместе (экраны, навигация, Firebase). Медленнее, но ближе всего к реальному использованию.
4. **Mockito** — библиотека для создания «заглушек» (моков) зависимостей. Позволяет задать, что вернёт зависимость (`when`), и проверить, как её вызывали (`verify`), поэтому тест не зависит от Firebase, сети и других внешних вещей.
