# Лабораторная №9 — Provider

Основа — приложение из лабы №8 (экран Hive: иконка-хранилище в шапке ленты → `HiveScreen`).

## Задание 1

**Условие:** Взять приложение из лабы №8.

**Где в коде:**
- Экран Hive из лабы 8: [hive_screen.dart](lib/screens/hive_screen.dart)
- Хранилище Hive: [hive_store.dart](lib/data/hive_store.dart)

**Как работает:** Боксы `users`, `posts`, `favorites` и вся работа с ними остались как в лабе 8. Изменилось только то, где хранится общее состояние экрана: раньше в `State` виджета, теперь в провайдере.

## Задание 2

**Условие:** Заменить все вызовы `setState`, связанные с глобальным/разделяемым состоянием, на Provider.

**Где в коде:**
- Модель состояния: [hive_model.dart](lib/data/hive_model.dart)
- `HiveScreen` стал `StatelessWidget`: [hive_screen.dart:10](lib/screens/hive_screen.dart#L10)
- Было `setState` → стало вызов модели:
  - выбор пользователя: [hive_screen.dart:46](lib/screens/hive_screen.dart#L46)
  - фильтр «только избранное»: [hive_screen.dart:51](lib/screens/hive_screen.dart#L51)
  - удаление поста: [hive_screen.dart:75](lib/screens/hive_screen.dart#L75)
  - лайк после возврата с экрана поста: [hive_screen.dart:25](lib/screens/hive_screen.dart#L25)
  - обновление списка после формы: [post_form_screen.dart:37](lib/screens/post_form_screen.dart#L37)

**Как работает:** Текущий пользователь, фильтр избранного и изменения постов/лайков нужны нескольким экранам, поэтому они переехали в `HiveModel`. Каждый метод модели меняет данные и вызывает `notifyListeners()` — все подписанные виджеты перерисовываются сами. Отдельный `setState(() {})` «чтобы обновить список» больше не нужен.

## Задание 3

**Условие:** Локальное состояние (например, `TextEditingController`) можно оставить через `setState` — обосновать, почему.

**Где в коде:**
- Поля формы (`TextEditingController`, без `setState`): [post_form_screen.dart:21-23](lib/screens/post_form_screen.dart#L21-L23)
- Выбранные картинки-чипы (`setState`): [post_form_screen.dart:24](lib/screens/post_form_screen.dart#L24), [post_form_screen.dart:58](lib/screens/post_form_screen.dart#L58)
- Лайк внутри экрана поста: [post_detail_screen.dart:78](lib/screens/post_detail_screen.dart#L78)
- Остальные локальные `setState`: [story_viewer_screen.dart:35](lib/screens/story_viewer_screen.dart#L35), [device_screen.dart:39](lib/screens/device_screen.dart#L39), [files_screen.dart:62](lib/screens/files_screen.dart#L62), [messages_screen.dart:30](lib/screens/messages_screen.dart#L30), [message_form_screen.dart:53](lib/screens/message_form_screen.dart#L53)

**Как работает:** Эти данные живут и нужны только внутри одного экрана: черновик формы, номер страницы в просмотре сторис, ответ платформы и т. п. Никто снаружи их не читает, а при закрытии экрана они должны исчезнуть. Если положить их в провайдер, они переживут экран (старый черновик появится в новой форме) и при каждом символе будут перерисовывать чужие виджеты. Поэтому для них `setState` — правильный и самый простой выбор. `TextEditingController` даже `setState` не требует: `TextField` сам слушает свой контроллер и перерисовывается при вводе, а текст читается из `controller.text` только при сохранении. В провайдер попадает только итог: форма вызывает `savePost`, экран поста возвращает лайк, а его сохраняет `setFav`.

## Задание 4

**Условие:** Обязательно использовать: `ChangeNotifierProvider` (минимум один), `MultiProvider` в `main.dart`, `Consumer` или `context.watch/select` минимум в двух местах, `context.read` для вызовов методов (без перерисовки).

**Где в коде:**
- `ChangeNotifier`: [hive_model.dart:6](lib/data/hive_model.dart#L6)
- `MultiProvider` + `ChangeNotifierProvider`: [main.dart:24-25](lib/main.dart#L24-L25)
- `context.watch`: [hive_screen.dart:35](lib/screens/hive_screen.dart#L35)
- `Consumer`: [feed_screen.dart:92-98](lib/screens/feed_screen.dart#L92-L98)
- `context.read`: [hive_screen.dart:18](lib/screens/hive_screen.dart#L18), [hive_screen.dart:46](lib/screens/hive_screen.dart#L46), [hive_screen.dart:51](lib/screens/hive_screen.dart#L51), [hive_screen.dart:75](lib/screens/hive_screen.dart#L75), [post_form_screen.dart:37](lib/screens/post_form_screen.dart#L37)

**Как работает:** `MultiProvider` стоит над `MaterialApp`, поэтому модель видна на всех экранах. `HiveScreen` через `watch` подписан на модель целиком и перестраивается при любом изменении. В ленте подписан только маленький `Consumer` с текстом — перерисовывается он один, а не весь экран. Кнопки вызывают методы через `read`: им нужно только «дёрнуть» метод, подписываться на изменения им незачем.

## Задание 5

**Условие:** Продемонстрировать передачу данных между двумя несвязанными виджетами (разные ветки дерева) через общий Provider.

**Где в коде:**
- Пишет: выпадающий список и ♥ в `HiveScreen`: [hive_screen.dart:46](lib/screens/hive_screen.dart#L46), [hive_screen.dart:25](lib/screens/hive_screen.dart#L25)
- Читает: текст `Admin ♥ 2` в ленте рядом с `Discover / Following`: [feed_screen.dart:92-98](lib/screens/feed_screen.dart#L92-L98)
- Общий предок: [main.dart:24-25](lib/main.dart#L24-L25)

**Как работает:** Лента (`FeedScreen`) и `HiveScreen` — два разных маршрута Navigator, друг друга не знают и ничего не передают через конструкторы. Обоим виден один `HiveModel`, потому что он стоит выше `MaterialApp`. В `HiveScreen` выбираем `Anna` или лайкаем пост → модель вызывает `notifyListeners()` → `Consumer` в ленте перерисовывается. Нажимаем «назад» — в ленте уже `Anna ♥ 1`.

## Задание 6

**Условие:** Не использовать `setState` для данных, которые уже в Provider.

**Где в коде:**
- В [hive_screen.dart](lib/screens/hive_screen.dart) нет ни одного `setState` (экран `StatelessWidget`)
- Все изменения общих данных идут только через методы модели: [hive_model.dart:14-37](lib/data/hive_model.dart#L14-L37)

**Как работает:** У данных один владелец — `HiveModel`. Пользователь, фильтр, посты и избранное не дублируются в `State` виджетов. Оставшиеся `setState` (задание 3) относятся к данным, которых в провайдере нет.

## Ответы на вопросы

**1. Что такое Provider и какую проблему он решает?**
Это пакет, который кладёт объект с данными в дерево виджетов, и любой виджет ниже может его получить. Без него данные приходится тащить через конструкторы всех промежуточных виджетов, а `setState` обновляет только свой виджет. Соседний экран так не обновить. Provider даёт общий источник данных и сам перерисовывает тех, кто на него подписан.

**2. На каком механизме основан provider и почему это важно?**
На `InheritedWidget` (через `InheritedElement` и `BuildContext`). Отсюда правила использования: провайдер ищется вверх по дереву от `context`, поэтому он должен стоять выше читающего виджета. Подписка делается через `context` в `build`. Перерисовываются только зависимые виджеты.

**3. Чем Provider отличается от InheritedWidget, какие плюсы у пакета?**
`InheritedWidget` только раздаёт неизменяемые данные. Чтобы их менять, нужен свой `StatefulWidget`, `updateShouldNotify` и много шаблонного кода. Provider делает это за вас: сам создаёт и удаляет объект (`create`/`dispose`), слушает `ChangeNotifier`, даёт `watch/read/select`, `Consumer`, `MultiProvider`, ленивое создание и понятные ошибки.

**4. Разница между `watch`, `read`, `select`?**
- `context.watch<T>()` — получить объект и подписаться: при любом `notifyListeners` виджет перестроится. Используется в `build`.
- `context.read<T>()` — получить объект без подписки. Нужен в обработчиках нажатий, чтобы вызвать метод.
- `context.select<T, R>(fn)` — подписаться только на часть данных: виджет перестроится, только если изменился результат `fn` (например, только имя пользователя).

**5. Почему `read` нельзя вызывать в `build()`?**
`read` не подписывает виджет. Если взять им данные в `build`, виджет покажет значение на момент первой отрисовки и дальше не обновится — UI «застынет» со старыми данными. Provider в debug-режиме ловит такой вызов и бросает ошибку-assert.

**6. Чем `Consumer<T>` отличается от `context.watch<T>()`?**
Результат тот же — подписка. `watch` подписывает весь виджет, в котором вызван, и при изменении перестраивается весь его `build`. `Consumer` — отдельный маленький виджет: перестраивается только его `builder`, а `child` можно передать неизменным. `watch` короче, он удобен, когда от данных зависит весь экран (`HiveScreen`). `Consumer` удобен, когда от данных зависит маленький кусок большого экрана (текст в ленте).

**7. Что такое `ChangeNotifier` и зачем наследовать?**
Это класс из Flutter, который хранит список слушателей и умеет их оповещать методом `notifyListeners()`. `ChangeNotifierProvider` подписывается на объект именно через этот интерфейс (`addListener`). Поэтому модель должна быть `ChangeNotifier`, иначе провайдеру не на что подписаться.

**8. Как обновить данные в `ChangeNotifier`?**
Вызвать метод модели, который меняет поле и в конце вызывает `notifyListeners()`. Например, `context.read<HiveModel>().setUser('Anna')`. После этого все виджеты с `watch`, `select` или `Consumer` на эту модель перестроятся в следующем кадре и покажут новые данные.

**9. Что будет, если изменить поле без `notifyListeners()`?**
Данные в объекте поменяются, но никто об этом не узнает: экран останется со старым значением. Новое значение появится только при случайной перерисовке по другой причине. Получается рассинхрон UI и данных.

**10. Почему нельзя создавать `ChangeNotifier` в `build()`?**
`build` вызывается много раз. Каждый раз будет создаваться новый объект: состояние сбрасывается, старые объекты не удаляются (утечки), подписчики теряются. Поэтому модель создают в `create:` провайдера: он создаёт её один раз и сам вызывает `dispose`. Если нужен готовый объект — `ChangeNotifierProvider.value`.

**11. Зачем `MultiProvider`?**
Чтобы объявить несколько провайдеров плоским списком. Вручную вложенные провайдеры работают так же: `MultiProvider` и превращается в такое вложение. Но код получается «лесенкой», его трудно читать и легко ошибиться со скобками и порядком.

**12. Как передать данные между несвязанными виджетами?**
Положить провайдер в их общего предка: выше обоих виджетов, обычно над `MaterialApp`, если это разные экраны. Один виджет меняет модель через `read`, второй подписан через `watch` или `Consumer` и перерисовывается. В этой лабе модель стоит над `MaterialApp`, а лента и `HiveScreen` — разные маршруты (задание 5).

**13. Что будет при обращении к провайдеру из виджета выше него?**
Поиск идёт только вверх, поэтому провайдер не найдётся. Будет исключение `ProviderNotFoundException` («Could not find the correct Provider<T> above this Widget»). Частая причина — `context` взят из того же `build`, где провайдер создаётся, или провайдер стоит внутри одного маршрута, а читают его из другого.

**14. Что такое `ProxyProvider` и `ChangeNotifierProxyProvider`?**
Это провайдеры, которые зависят от других провайдеров: при их изменении в `update` создаётся или обновляется зависимый объект. Например, корзина зависит от текущего пользователя. `ProxyProvider` отдаёт обычное значение и при каждом `update` может создавать его заново. `ChangeNotifierProxyProvider` работает с `ChangeNotifier`: объект создаётся один раз в `create`, в `update` ему передают новые зависимости, а провайдер слушает его `notifyListeners` и сам вызывает `dispose`.
