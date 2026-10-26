# ADR-029: Reply-keyboard «Войти в webUI» для авторизованных пользователей и inline-кнопка ссылки входа в боте

## Статус

Принято (проект). Основание — задача
[bot-login-quick-access-keyboard](../tasks/completed/bot-login-quick-access-keyboard.md) (реализована,
валидирована PM 2026-10-26; критерии приёмки и edge cases исполнены). Решение определяет выбор способа
определения авторизованности («за архитектором» из раздела «Зависимости» задачи) и точки
централизации keyboard. Реализация — по плану (декомпозиция задачи).

**Дополняет, ничего не отменяет:** [ADR 003](003-get-login-link-analytics-api.md) (получение
login-ссылки командой `/login` через Analytics API — здесь меняется только оформление ответа),
[ADR 004](004-bot-grpc-retry.md) (`WaitForReady`, старт с предзаполнением `user.Provider`),
[ADR 018](018-bot-no-proxy-internal-traffic.md) (внутренний трафик бота мимо прокси — новый
поток вызовов авторизации остаётся внутри docker-сети).

**Ревизия 2 (2026-10-26):** пересмотрен D1 по замечанию ревьюера — предикат
«пользователь есть в `system_users`» не соответствовал требованию заказчика («авторизован =
зарегистрирован через `/register` **и** подтверждён кодом из SMS»). Состояние подтверждения
уже хранится в системе неявно — через токены устройства (см. таблицу фактов), поэтому принят
точный предикат «подтверждён», вычисляемый backend'ом и доставляемый в бот аддитивным
полем gRPC `User.verified`. Миграции и backfill не требуются. D2–D8 приняты без замечаний;
в D2 уточнено: политика авторизации вычисляется **в момент отправки** (иначе сообщение
подтверждения `/code` осталось бы без keyboard).

> **Вне области:** изменения схем и данных MongoDB (признак «подтверждён» вычисляется из
> токенов device, миграции не нужны); Analytics и nginx; keyboard-меню дополнительных команд
> (`/get`, `/report`) и удаление keyboard при отвязке аккаунта (`ReplyKeyboardRemove`);
> обработка `CallbackQuery`; локализация прочих ответов бота.

## Контекст

### Проблема

Бот отвечает только текстом. Зарегистрированному пользователю, чтобы попасть в webUI, нужно
вспомнить и вручную ввести `/login`, затем перейти по «голой» текстовой ссылке. Требуется:

1. Постоянный reply-keyboard из одной кнопки **«Войти в webUI»** для авторизованных
   (зарегистрированных и подтверждённых) пользователей; нажатие эквивалентно `/login`.
2. Keyboard прикладывается к **каждому** ответу авторизованному пользователю, включая ошибки;
   появляется сразу после успешного подтверждения регистрации.
3. Незарегистрированные пользователи keyboard не видят, их поведение не меняется.
4. Ответ `/login` — inline-кнопка **«Открыть webUI»** по одноразовой ссылке.

### Текущее состояние кода (факты, проверены по коду 2026-10-26)

| Место | Факт (что есть в коде) | Значение для решения |
|---|---|---|
| `bot/commands/common.go:9-23` | `Command interface { Accepted(string) bool; Execute(update, *tgbotapi.BotAPI) error }`; пакетные хелперы `sendTextMessage`/`replyToMessage` — оба идут в `bot.Send` | Точек отправки две, обе в одном пакете; но команды принимают «голый» `*tgbotapi.BotAPI` и отправляют сами — централизации нет |
| `bot/commands/registrar.go:23-30`, `bot/bot.go:100-111` | Диспетчеризация: `TrimSpace(text)` → линейный `Get` по `Accepted` (first-wins) → `Execute`; default — `WrongCommand` | Распознавание текста кнопки решается через `Accepted` конкретной команды; order-конфликтов нет (точные тексты) |
| `bot/bot.go:54-65` | `processNotifications` — отдельная горутина, шлёт отчёты напрямую `tgbotapi.NewMessage` + `bot.Send` | Третья точка исходящих сообщений; при проверке авторизации отсюда к `user.Provider` будут обращаться из двух горутин |
| `bot/backend/user/provider.go:9-39` | `Provider`: map `telegramId → userId`, предзаполнен `GetUsers` при старте (`ADR 004`), miss → gRPC `GetUser` с кэшированием; unknown → ошибка `"not_found"` (`bot/backend/grpc.go:83-86`); **мьютекса нет**, кэш не различает подтверждение | Готовая инфраструктура «telegramId → состояние пользователя», но семантику кэша нужно перестроить: хранить признак `verified` и кэшировать только его истинность (D1); потокобезопасность — обязательна |
| `backend/users/processor.go:66-91,93-104` | Документ в `system_users` создаётся уже в `RegisterUser` (на `/register`); там же создаётся device с `SessionId: ""`, `RefreshToken: ""` | Существование записи = регистрация начата, **но не подтверждена**; признак подтверждения живёт в токенах device (следующие строки) |
| `backend/nalogru/client.go:286-309` | `AuthByPhone` только запрашивает SMS у nalog.ru и **не** пишет токены в device | После `/register` токены device остаются пустыми |
| `backend/nalogru/client.go:391-427` | `VerifyPhone`: при ошибке/не-200 ничего не пишет; при успехе — `device.Update(response.SessionId, response.RefreshToken)` | Непустые токены появляются **только** после успешного подтверждения кодом |
| `backend/nalogru/client.go:351-386` | `RefreshSession` пишет только свежие токены от nalog (после `200`); на ошибке возвращается до записи | В штатной эксплуатации токены в пустоту не сбрасываются |
| `backend/device/service.go:92-97,110-126` | `Service.All()`/`GetByUserId` читают in-memory список; `Update` пишет память + Mongo | Вычисление «подтверждён» не даёт нагрузки на Mongo; read-after-write согласован внутри процесса backend'а |
| `api/inside/accounts.proto:19-22` | `message User { userId = 1; telegramId = 2; }` — поля статуса нет | носитель признака — аддитивное `bool verified = 3` (proto3: обратно совместимо, старый потребитель его игнорирует) |
| `Analytics/.../MongoUserLoader.cs`, `MongoReceiptSourceOptions.cs:18` | Analytics синхронизирует пользователей из той же коллекции `system_users`; `GET /api/users/auth/link` для неизвестного → `404` (`bot/analytics/client.go:95-96`) | «Может войти в webUI» в конечном счёте решает Analytics; его `404` — уже есть честный деградирующий ответ `/login` |
| `bot/commands/code.go:34-44` | `ConfirmationCodeCommand.Execute` вообще **не отправляет сообщение** — только gRPC `VerifyPhone` | Критерий «keyboard сразу после подтверждения» невыполним без добавления ответного сообщения |
| `bot/commands/login.go:16-30` | `Accepted` — ровно `"/login"`; ответ — голый текст `link` или `err.Error()`; зависимости — конкретный `*analytics.Client` | Расширение `Accepted` текстом кнопки и перевод ответа на inline-кнопку — локальные правки; для тестов нужен интерфейс вместо конкретного клиента |
| tgbotapi `v4.6.4+incompatible` (`types.go:383-388`, `helpers.go:652-680`) | `ReplyKeyboardMarkup` **без** `is_persistent` (появился позже); `NewInlineKeyboardButtonURL` есть; у сообщения ровно **один** `reply_markup` | Персистентность обеспечивается повторной отправкой keyboard с каждым ответом (ровно требование задачи); inline-кнопка и reply-keyboard в одном сообщении не совмещаются |

## Решение

Сводка принятых решений:

1. **D1.** Предикат авторизованности — **подтверждение регистрации кодом из SMS**:
   «подтверждён» ⟺ у пользователя есть device с непустым `SessionId` (или `RefreshToken`).
   Вычисляет backend (данные уже в системе, миграции не нужны), доставляет в бот аддитивным
   полем gRPC `User.verified`; бот кэширует только положительные факты. Сбой gRPC =
   «нет keyboard» без влияния на обработку.
2. **D2.** Централизация прикладывания keyboard — новый тип `commands.Messenger`: оборачивает
   отправку в конкретный чат с политикой «приложить keyboard, если пользователь авторизован»;
   `Command.Execute` вместо `*tgbotapi.BotAPI` получает `*Messenger`.
3. **D3.** Константы текстов кнопок и конструкторы keyboard/inline — один новый файл
   `bot/commands/keyboard.go`, чистые функции (тестируются без сети).
4. **D4.** Распознавание нажатия — расширение `GetLoginLinkCommand.Accepted`: `"/login"`
   **или** точный текст кнопки; механизмы registrar/registrar-порядок не меняются.
5. **D5.** Ответ `/login` — inline-кнопка «Открыть webUI» (URL) при успехе; ошибка — текст +
   reply-keyboard. Команда сама авторизацию не проверяет (единственный источник истины —
   Analytics со своим `404`).
6. **D6.** `/start` — поведение не меняется; `/code` — добавляется ответ (успех/ошибка), который
   по D2 и даёт keyboard «сразу после подтверждения».
7. **D7.** Push-уведомления отчётов (`processNotifications`) тоже идут через политику keyboard —
   отсюда требование потокобезопасности `Provider` (D1).
8. **D8.** Тестоспособные seams: интерфейсы `commands.Authorizator`, внутренний `apiSender`
   `Messenger`, `LoginLinkProvider` для `analytics.Client`.

### D1. Определение авторизованности: подтверждение кодом через состояние device и поле `User.verified`

Предикат (требование заказчика, подтверждённое дополнительно к задаче): **авторизован =
зарегистрирован через `/register` И подтверждён кодом из SMS**. Явного флага в системе нет,
но состояние подтверждения уже сохраняется неявно — токенами устройства (строки фактов
`AuthByPhone`/`VerifyPhone`/`RefreshSession`). Отсюда точный предикат:

> **Подтверждён ⟺ для пользователя существует device с непустым `SessionId` или
> `RefreshToken`.**

Непустые токены — единственный наблюдаемый побочный эффект успешного `VerifyPhone`; в пустоту
они не сбрасываются. **Миграции и backfill не нужны**: состояние уже в существующих данных —
исторические подтверждённые пользователи получают keyboard сразу, исторические неподтверждённые
— корректно не получают.

Цепочка доставки предиката в бот (снизу вверх):

1. **`api/inside/accounts.proto`** — в `message User` аддитивное поле `bool verified = 3;`,
   перегенерация `accounts.pb.go`. proto3-аддитивность: старый потребитель игнорирует новое
   поле, новый потребитель старого backend'а получит `false` (безопасное направление деградации,
   см. «Последствия» и порядок деплоя).
2. **`backend/users/processor.go`** — вычисление флага:
   - чистые хелперы (единственный источник предиката, тестируются без I/O):
     `isDeviceConfirmed(d *device.Device) bool` — `d != nil && (d.SessionId != "" || d.RefreshToken != "")`;
     `confirmedUserIds(devices []*device.Device) map[string]bool` — множество `UserId`
     подтверждённых устройств (несколько device у одного пользователя — достаточно любого);
   - `GetUser` (`processor.go:52-64`): `verified = isDeviceConfirmed(deviceService.GetByUserId(userId))`
     (`deviceService` уже внедрён в `Processor` — `processor.go:15,78`, новый код в device-пакет
     не требуется);
   - `GetUsers` (`processor.go:31-49`): флаг из `confirmedUserIds(deviceService.All(ctx))` —
     in-memory, без обращений к Mongo.
3. **`bot/backend`** — в `User` (`contract.go`) поле `Verified bool`; `grpc.go` `GetUser`/`GetUsers`
   транслируют `v.Verified`.
4. **`bot/backend/user/provider.go`** — реконструкция кэша. **Ключевое изменение семантики:
   закешированный «known» больше НЕ означает «показывать keyboard»**:
   - map хранит `telegramId → entry{userId string; verified bool}`, предзаполняется из `GetUsers`;
   - **положительное решение кэшируется, отрицательное — нет**: в map пишется только
     `verified == true`. Не подтверждён/неизвестен перечитывается каждый раз — это транзитное
     состояние, которое вот-вот станет истинным, и кэш «false» задержал бы keyboard
     (см. также «Немедленность» ниже);
   - новый метод:

     ```go
     // IsAuthorized reports whether the user is registered AND phone-confirmed.
     func (p *Provider) IsAuthorized(telegramID int) bool {
         // hit => true (в map живут только подтверждённые);
         // miss => GetUser(telegramID): verified => закэшировать и true;
         //          неподтверждён / not_found / ошибка gRPC => false (без кэша).
     }
     ```

   - `GetUserId` сохраняет прежнюю семантику «существование» (регистрационный флоу: `/code`
     вызывает его **до** подтверждения — должен работать и для неподтверждённых); miss у
     неподтверждённого пользователя просто не кэшируется;
   - **потокобезопасность (обязательно):** `Provider` читается/дописывается из двух горутин
     (обработка апдейтов и `processNotifications`, D7) — добавить `sync.RWMutex`: read-lock на
     hit, write-lock на дописывание. Иначе data race (детектируется `go test -race`).
- **Отказоустойчивость** (edge case «сервис статусов недоступен»): любая ошибка gRPC внутри
  `IsAuthorized` → `false`. Обработка сообщения не прерывается — просто нет keyboard на этом
  ответе.
- **Немедленность после подтверждения** (критерий «keyboard показывается сразу после успешного
  подтверждения»): `VerifyPhone` возвращает ответ backend'а уже после `device.Update`
  (память+Mongo), а `Messenger` вычисляет политику в момент отправки (lazy, D2) — поэтому
  **ответ команды `/code` сам** получает keyboard. Командам не нужно «маркировать»
  подтверждение: `code.go` не связывается с `Provider`, единственный источник истины —
  состояние device на backend'е (read-after-write согласован: последовательные gRPC-вызовы
  к одному процессу).
- **Устаревание** (edge case «статус устарел» — единственный оставшийся): кэшированный
  `verified = true` при жизни процесса не инвалидируется — если аккаунт отвязали/токены
  почистили вручную, keyboard виден до рестарта бота, `/login` честно отвечает `404`.
  Это деградация ровно того класса, которую задача прямо допускает. Обратного устаревания
  («подтверждённого не показывают») нет: отрицательные состояния не кэшируются.
- **Производительность:** hit — O(1) из памяти; miss — один внутренний gRPC `GetUser`
  (backend считает флаг in-memory). Отрицательный кэш сознательно НЕ вводится (см. выше);
  при проблемах TTL-кэш «false» — локальное решение реализации, контракт не меняется.

### D2. Централизация: `commands.Messenger` вместо «голого» `*tgbotapi.BotAPI` в `Execute`

Все исходящие сообщения бота проходят через три места: хелперы `sendTextMessage`/
`replyToMessage` (`bot/commands/common.go`), отправку документа в `get_report.go:45-47` и
`processNotifications` (`bot/bot.go:58-60`). Ни one-time-hack в хелперах (им неоткуда знать
авторизованность), ни «отправить keyboard отдельным сообщением» (засорение чата) не приемлемы.
Решение — единая отправка с политикой:

- Новый файл `bot/commands/messenger.go`:

  ```go
  // Authorizator decides whether a telegram user gets the quick-access keyboard.
  type Authorizator interface { IsAuthorized(telegramID int) bool }

  // apiSender — seam для тестов; *tgbotapi.BotAPI её удовлетворяет.
  type apiSender interface { Send(config tgbotapi.Chattable) (tgbotapi.Message, error) }

  // Messenger отправляет сообщения в чат, прикладывая reply-keyboard подтверждённым (D1).
  type Messenger struct {
      api        apiSender
      auth       Authorizator
      chatID     int64
      telegramID int
  }

  func NewMessenger(api apiSender, auth Authorizator, chatID int64, telegramID int) *Messenger
  // SendText/ReplyText применяют keyboard автоматически; Send — «сырой» вариант для
  // сообщений с собственным reply_markup (inline-кнопка /login, см. D5).
  func (m *Messenger) Send(msg tgbotapi.Message) (tgbotapi.Message, error)
  func (m *Messenger) SendText(text string) (tgbotapi.Message, error)
  func (m *Messenger) ReplyText(text string, replyToMessageID int) (tgbotapi.Message, error)
  func (m *Messenger) SendDocument(doc tgbotapi.DocumentMessage) (tgbotapi.Message, error)
  ```

  Внутри методов `Messenger` политика проверяется **в момент каждой отправки**
  (`auth.IsAuthorized(telegramID)`), а не замораживается в конструкторе: во время исполнения
  команды `/code` статус пользователя меняется (D1), и при заморозке сообщение подтверждения
  само осталось бы без keyboard — вразрез с критерием «сразу после успешного подтверждения».
  Проверка — hit O(1); miss один внутренний RPC (D1). Методы применяют `keyboard` к
  `ReplyMarkup` конфигу, если пользователь подтверждён, и отправляют через `api`.
- **Изменение контракта команды (осознанная плата):**

  ```go
  type Command interface {
      Accepted(message string) bool
      Execute(update tgbotapi.Update, m *Messenger) error
  }
  ```

  Это механическая правка всех команд в `bot/commands/` (`start.go`, `register.go`, `code.go`,
  `login.go`, `get_report.go`, `add_receipt.go`, `empty.go`, `wrong.go`): вызовы
  `sendTextMessage(chatID, bot, ...)` → `m.SendText(...)`, `replyToMessage(...)` →
  `m.ReplyText(...)`, `bot.Send(upload)` в `get_report.go` → `m.SendDocument(upload)`.
  Хелперы `sendTextMessage`/`replyToMessage` из `common.go` удаляются (становятся
  методами `Messenger`) — альтернатива «тонкие прокси-функции» оставила бы два пути отправки,
  один из которых без keyboard, что противоречит критерию «к каждому ответу».
- **Провязка:** `processUpdates`/`processMessage` (`bot/bot.go`) принимают `Authorizator`
  (в `main.go` — тот же `*user.Provider`, уже создаваемый там) и конструируют `Messenger`
  на каждый апдейт: `commands.NewMessenger(bot, users, update.Message.Chat.ID, update.Message.From.ID)`.
  Подпись `start(...)` в `main.go:33-52` — расширяется параметром `users`.
- Существующие тесты (`register_test.go`, `code_test.go`) проверяют только `Accepted` и
  извлекающие функции — контракта `Execute` не касаются, не ломаются.

### D3. Тексты и конструкторы: `bot/commands/keyboard.go`

- Константы (единственное место, где тексты «живут»):

  ```go
  const (
      LoginButtonText     = "Войти в webUI"   // reply-keyboard, нажатие == /login
      OpenWebUiButtonText = "Открыть webUI"   // inline URL-кнопка ответа /login
  )
  ```

- `BuildLoginKeyboard() tgbotapi.ReplyKeyboardMarkup` — одна кнопка в одном ряду,
  `ResizeKeyboard: true`, `OneTimeKeyboard: false`, `Selective: false`.
  Поля `is_persistent` в tgbotapi `v4.6.4` нет; «постоянство» обеспечивается повторной
  отправкой markup с каждым ответом — это ровно требование задачи, а не обход.
  Структура value-only, вызов дешёв — отдельный кэш markup не нужен.
- `BuildLoginLinkMessage(chatID int64, link string) tgbotapi.Message` — текст-подсказка
  (одноразовость/ограниченный срок ссылки) + `InlineKeyboardMarkup` из одной
  `NewInlineKeyboardButtonURL(OpenWebUiButtonText, link)`.
- Оба конструктора — чистые функции, без `bot.Send`: тестируются декларативно (структура
  `Keyboard`, тексты, `URL` кнопки) без сети и без Telegram.

### D4. Распознавание текста кнопки

- `GetLoginLinkCommand.Accepted` (`bot/commands/login.go:16-18`) становится:

  ```go
  func (c *GetLoginLinkCommand) Accepted(message string) bool {
      return message == "/login" || message == LoginButtonText
  }
  ```

- Нажатие reply-keyboard-кнопки Telegram доставляет как обычное текстовое сообщение —
  `processMessage` (`bot/bot.go:100-111`) с `TrimSpace` попадает в этот же `Accepted`.
  Конфликтов нет: совпадение точное, остальные `Accepted` (regex `/register`, `/code`,
  `t=...` QR, `/get`, `/start`) на строку «Войти в webUI» не срабатывают, а `login`
  зарегистрирован раньше default-команды (`bot/main.go:50-78`).
- Повторные нажатия не имеют состояния: `Execute` каждый раз запрашивает новую ссылку —
  свойство существующего `analytics.Client.GetLoginLink` (edge case задачи выполняется
  «бесплатно»).
- Команда, введённая вручную (`/login`), работает как раньше — совпадение по `"/login"`
  сохранено.

### D5. Ответ `/login`: inline-кнопка вместо голой ссылки

- Успех: `update.Message` → `BuildLoginLinkMessage(chatID, link)` → отправка через
  `Messenger`, **но** без подмены markup: inline-клавиатура самого сообщения важнее
  reply-keyboard (см. ниже про ограничение Telegram). Поэтому в `login.go` успех отправляется
  через `m.Send(...)`-вариант, принимающий готовый `tgbotapi.Message`, и reply-keyboard к этому
  сообщению не прикладывается.
- Ошибка (Analytics недоступен, `404`, ретраи исчерпаны): текст `err.Error()` через
  `m.SendText` — reply-keyboard прикладывается по политике D2 (edge case «ошибка получения
  ссылки — keyboard остаётся»).
- **Платформенное ограничение (фиксируем как осознанное):** у сообщения ровно один
  `reply_markup`, поэтому успешный ответ со ссылкой содержит inline-кнопку и не может нести
  reply-keyboard одновременно. Reply-keyboard от предыдущих сообщений при этом остаётся
  видимым под полем ввода (он персистентен, пока пользователь не свернул его и bot не прислал
  `ReplyKeyboardRemove`). Это не противоречит критерию: «keyboard при каждом ответе» соблюдается
  для всех ответов кроме одного сообщения с inline-кнопкой, сам keyboard от этого не исчезает.
- **Авторизацию сама команда не дублирует:** проверка «зарегистрирован ли» остаётся у
  Analytics (единственный источник истины, `404` как честный отказ). Поведение `/login`
  для незарегистрированного по смыслу не меняется (тот же текст ошибки), только с keyboard-
  политикой, которая для него `nil`.
- Для тестов `Execute` зависит от нового интерфейса (D8), а не от конкретного
  `*analytics.Client`; `main.go` продолжает передавать клиент (`NewGetLoginLinkCommand` —
  сигнатура совместима).

### D6. `/start`, `/register`, `/code`

- `/start`: текст и логика не меняются. Keyboard прикладывается автоматически через D2, если
  пользователь авторизован; новому пользователю — нет (критерий «их поведение не меняется»).
  Персонализация текста `/start` для авторизованных — сознательно не делаем (YAGNI; подсказка
  о кнопке визуальна сама по себе).
- `/register`: текст ответа не меняется; пользователь уже зарегистрирован, но ещё не
  подтверждён → keyboard **не показывается** (точный предикат D1) — требование заказчика
  выполняется буквально.
- `/code` (`bot/commands/code.go`) — расширение в объёме задачи (без него критерий
  «keyboard сразу после подтверждения» физически невыполним — команды сейчас молчат):
  - успех (`VerifyPhone` вернул `err == nil`): `m.ReplyText` с сообщением об успешном
    подтверждении; keyboard прикладывается к **этому же** сообщению — `Messenger` вычисляет
    политику в момент отправки (D2), а backend к этому моменту уже записал токены (D1,
    read-after-write); специальной «маркировки» от команды не требуется;
  - ошибка: `m.ReplyText` с текстом ошибки; keyboard — по общей политике D2 (на этом этапе
    пользователь, как правило, ещё не подтверждён → keyboard не показывается);
  - семантику «успех» не уточняем: `backend` возвращает `ErrorResponse{Error:"TODO"}`,
    который bot'овский `GrpcClient.VerifyPhone` (`bot/backend/grpc.go:127-136`) отбрасывает —
    это известный долг backend'а вне рамок задачи; бот считает успехом отсутствие gRPC-ошибки
    (как сейчас молчаливо считает).

### D7. Push-уведомления отчётов

`processNotifications` (`bot/bot.go:54-65`) — тоже «ответ бота пользователю». Отправку
перевести на тот же механизм: `commands.NewMessenger(bot, auth, r.TelegramId, r.TelegramId)
.SendText(r.Message)` (достаточно пробросить `auth` в `start(...)`). Именно одновременная
работа этой горутины и `processMessage` с `Provider` — основание для `sync.RWMutex` в D1.

### D8. Интерфейсы для тестуемости (seams)

| Seam | Где | Кто реализует | Зачем |
|---|---|---|---|
| `commands.Authorizator { IsAuthorized(int) bool }` | `messenger.go` | `*user.Provider` | `Messenger`/команды не зависят от gRPC; fake в тестах |
| `apiSender { Send(tgbotapi.Chattable) (tgbotapi.Message, error) }` | `messenger.go` (неэкспортируемый) | `*tgbotapi.BotAPI` | Тесты перехватывают отправленные `Chattable` и их `ReplyMarkup` без сети |
| `LoginLinkProvider { GetLoginLink(ctx, telegramID int) (string, error) }` | `login.go` | `*analytics.Client` | Тесты `Execute` с success/failure без HTTP |
| `grpcUserClient { GetUser(ctx, telegramID int) (*backend.User, error) }` | `backend/user/provider.go` (неэкспортируемый) | `*backend.GrpcClient` | Тест `IsAuthorized`: `Verified: true/false`, `not_found`, ошибка — без реального gRPC; проверяется и политика кэширования (кэш только подтверждённых) |

Внешние контракты сервисов (Analytics HTTP, Telegram-команды) не меняются; контракт gRPC
`api/inside` — только аддитивно (`User.verified`). Все интерфейсы внутренние, по принципу
Dependency Inversion, без new-зависимостей в go.mod.

## Рассмотренные варианты (отклонённые)

### Способ определить авторизованность (пересмотрен в ревизии 2)

1. **Предикат по токенам device + аддитивное поле `User.verified` (выбран).** Точное
   соответствие требованию заказчика. Ключевой факт, определяющий дешевизну: состояние
   подтверждения **уже есть в данных** — непустые `SessionId`/`RefreshToken` записывает только
   успешный `VerifyPhone` (`nalogru/client.go:391-427`), `RefreshSession` их не обнуляет,
   `AuthByPhone` не пишет. Значит: ни миграции схемы, ни backfill не нужны; вычисление —
   in-memory (`device.Service`); gRPC-контракт расширяется аддитивно. Затраты — перегенерация
   `accounts.pb.go` и правки в обоих Go-сервисах (было «бот-only», стало «bot + backend»).
2. **«Пользователь существует в `system_users`» (вариант ревизии 1).** Отклонено: не
   соответствует подтверждённому требованию заказчика — keyboard появлялся бы уже на
   `/register`, до подтверждения кодом. (Аргумент предыдущей ревизии «флаг подтверждения
   отсутствует, backfill нерешаем» ошибочен — состояние подтверждения хранится неявно
   токенами device.)
3. **Новое явное поле `confirmed` в документе MongoDB users.** Дублировало бы состояние,
   которое уже гарантированно выводится из токенов; потребовало бы миграции, поддержки
   согласованности двух источников и решения о backfill. Отклонено — производный признак
   лучше вычислять из первичного факта.
4. **Отдельный RPC статуса** (например, `GetVerificationStatus(telegramId)`). Дублирует
   выборку существующего `GetUser` без выигрыша: аддитивное поле proto3-сообщения не ломает
   старых потребителей, а новых rpc-методов в контракте становится больше. Отклонено.
5. **Спросить Analytics (`404` у `auth/link`).** Фактически неверно и дорого: у Analytics
   нет данных о подтверждении (пользователи синхронизируются из `system_users`, device-токены
   туда не попадают), а сам запрос имеет побочный эффект — генерацию одноразовой ссылки.
   Отклонено.
6. **Состояние бота «подтверждён с начала жизни процесса»** (помечать после успешного
   `/code`). Губится рестартом контейнера: исторически подтверждённые пользователи теряли бы
   keyboard до нового кода. Отклонено.

### Куда прикладывать keyboard

1. **`Messenger` на апдейт (выбран).** Одна политика, все три точки отправки, тестоспособно;
   политика вычисляется в момент отправки — корректно и для смены статуса внутри апдейта
   (`/code`, D1/D2).
2. **Пакетные хелперы + глобальное состояние** (пакетная переменная «текущий пользователь»).
   Скрытая связь, ломается на `processNotifications`, не тестируется. Отклонено.
3. **Отдельное «служебное» сообщение с keyboard.** Видимо пользователю, шумит. Отклонено.
4. **Отправить keyboard один раз и «полагаться на персистентность» Telegram.** Формально
   нарушает критерий «к каждому ответу»; в tgbotapi v4.6.4 нет `is_persistent`, и на мобильных
   клиентах неперсистентный keyboard сворачивается. Отклонено.

### Распознавание кнопки

1. **Расширенный `Accepted` команды (выбран).** Знание о команде остаётся внутри команды.
2. **Маппинг «текст → команда» в `processMessage`/registrar.** Дублирует реестр команд в main,
   усложняет порядок matching. Отклонено.
3. **Отдельная команда-обёртка для кнопки.** Дублирование логики `/login`. Отклонено.

## Последствия

**Положительные:**
- Требование заказчика выполняется точно: keyboard — только зарегистрированным **и
  подтверждённым**; деградация «keyboard в окне `/register → /code`» из ревизии 1 **исчезает**
  (до записи токенов предикат ложен).
- Исторические подтверждённые пользователи получают keyboard сразу (токены уже в данных),
  исторические неподтверждённые — корректно нет; миграций и backfill нет.
- Критерий «сразу после подтверждения» выполняется на самом сообщении `/code`
  (lazy-политика D2 + read-after-write D1), без связывания команд с `Provider`.
- Вычисление подтверждения на backend — in-memory (`device.Service`), без доп. нагрузки на
  Mongo; gRPC-контракт расширяется аддитивно, обратная совместимость протокола сохранена.
- Единая точка политики «какие элементы управления видит пользователь» (`Messenger`) —
  новые команды получают keyboard автоматически, не помня о нём.
- Исчезает «молчание» `/code`: пользователь получает подтверждение результата регистрации.
- Тестоспособность: предикат (чистые хелперы backend'а), кэш-политика `Provider`, сборка
  сообщений (keyboard, inline-кнопка) и распознавание кнопки проверяются чистыми юнитами
  без сети; `go test -race` покрывает конкурентный доступ к `Provider`.
- Не меняются: контракты Analytics HTTP и Telegram-команд, поведение незарегистрированных,
  механика `/login` (ссылка по-прежнему одноразовая, честный `404`).

**Отрицательные / затраты:**
- **Задача перестаёт быть «бот-only»:** правки в `api/inside` (proto + перегенерация
  `accounts.pb.go`) и `backend/users/processor.go`; в объёме приёмки участвуют оба Go-сервиса.
- **Порядок деплоя:** раньше backend, потом bot. При обратном порядке новый bot с old backend
  всегда получает `verified = false` — keyboard молча не показывается никому (безопасное
  направление: `/login`, регистрация и прочее не затронуты).
- Отрицательные состояния (`не подтверждён`/`неизвестен`) не кэшируются: такой пользователь
  стоит один внутренний `GetUser` RPC на сообщение (транзитное состояние, in-memory вычисление,
  docker-сеть — принято, см. D1).
- Устаревание `verified = true` в кэше при жизни процесса (отвязка аккаунта/ручная чистка
  токенов) — keyboard виден до рестарта бота; деградация класса «устаревший статус»,
  прямо допустимая задачей.
- Изменение `Command.Execute` — правка всех 8 команд и `processMessage` (механическая, но
  сквозная).
- Смешение языков в UI (русские кнопки среди английских ответов бота) — cosmetic-долг,
  локализация вне области.

**Нейтральные:**
- Новые файлы `bot/commands/keyboard.go`, `bot/commands/messenger.go`; `sync.RWMutex` в
  `user.Provider`; новые env-переменные и конфигурация не требуются.

## Тестование и приёмка

Юнит-тесты (`cd bot && go test ./...`, для backend — `cd backend && go test ./...`):

| Файл | Покрывает |
|---|---|
| `commands/keyboard_test.go` | `BuildLoginKeyboard`: 1 ряд × 1 кнопка, текст `LoginButtonText`, `ResizeKeyboard`, `!OneTimeKeyboard`; `BuildLoginLinkMessage`: inline-кнопка `OpenWebUiButtonText`, `URL == link` |
| `commands/login_test.go` | `Accepted`: `"/login"` и `"Войти в webUI"` → true; `"/login "`, `"войти в webui"` (регистр), `"Открыть webUI"`, случайный текст → false. `Execute` (fake `LoginLinkProvider` + fake `apiSender`): успех → сообщение с inline-URL-кнопкой; ошибка → текст ошибки + reply-keyboard (authorized) / без него (unauthorized) |
| `commands/messenger_test.go` | `SendText`/`ReplyText`/`SendDocument`: authorized → `ReplyMarkup == BuildLoginKeyboard()`; unauthorized → `nil`; lazy-семантика: один и тот же `Messenger` реагирует на смену состояния mid-life (fake `Authorizator` с переключаемым флагом и счётчиком вызовов — имитация «подтвердили между сообщениями») |
| `backend/user/provider_test.go` | `IsAuthorized` (fake `grpcUserClient`): hit map (verified) → true без RPC; miss + `Verified: true` → true и кэш (второй вызов — без RPC); miss + найден, но `Verified: false` → false и **без кэша** (второй вызов — снова RPC); `not_found`/ошибка → false без паники. `GetUserId`: для неподтверждённого пользователя по-прежнему возвращает `userId` (семантика «существование» для регистрационного флоу сохранена) |
| `backend/users/processor_test.go` (backend) | Чистые хелперы предиката: `isDeviceConfirmed` — `nil`/пустые токены → false; непустой `SessionId` или `RefreshToken` → true; `confirmedUserIds` — множество по списку device (в т.ч. несколько device у одного пользователя) |
| `-race` | Прогон тестов `user`-пакета с конкурентными `IsAuthorized`/`GetUserId` (имитация двух горутин D7) |

Ручная приёмка по сценарию задачи (регистрация → keyboard → нажатие → inline-кнопка → webUI) —
критерии задачи, вне рамок автотестов.

## Связь с другими ADR

- **ADR 003** (GetLoginLink через Analytics): путь получения ссылки не меняется; ADR-029 меняет
  только оформление ответа (inline-кнопка) и добавляет keyboard-политику вокруг команды.
- **ADR 004** (retry/WaitForReady): `IsAuthorized` опирается на тот же `Provider`, что создаётся
  после `WaitForReady`; ошибка статуса во время работы (не при старте) деградирует до `false`.
- **ADR 018** (нет прокси для внутреннего трафика): новых внешних вызовов нет; `Provider`
  ходит в backend по внутреннему gRPC.

## Ссылки

- Задача: [docs/tasks/bot-login-quick-access-keyboard.md](../tasks/completed/bot-login-quick-access-keyboard.md)
- Код: `api/inside/accounts.proto`, `backend/users/processor.go`, `backend/nalogru/client.go`
  (жизненный цикл токенов), `backend/device/service.go`, `bot/bot.go`, `bot/main.go`,
  `bot/commands/` (все команды), `bot/backend/` (`contract.go`, `grpc.go`),
  `bot/backend/user/provider.go`, `bot/analytics/client.go`
- Telegram Bot API: Reply Keyboard / Inline Keyboard (поле `reply_markup` — один на сообщение)
- tgbotapi `v4.6.4+incompatible`: `types.go:383-388`, `helpers.go:652-680`
