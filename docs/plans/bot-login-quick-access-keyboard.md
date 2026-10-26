# План реализации: reply-keyboard «Войти в webUI» в боте + предикат «подтверждён» в backend

> Декомпозиция утверждённой задачи для команды разработки. Источники:
> [задача bot-login-quick-access-keyboard](../tasks/completed/bot-login-quick-access-keyboard.md)
> (критерии приёмки и edge cases обязательны к исполнению),
> [ADR-029](../adr/029-bot-login-reply-keyboard.md) **ревизия 2 — финальная** (решения D1–D8
> приняты), дополняет ADR 003 (получение login-ссылки), ADR 004 (`WaitForReady`, предзаполнение
> `Provider`), ADR 018 (внутренний трафик бота мимо прокси).
>
> **Новых архитектурных решений план не содержит.** Всё ниже — порядок работ, файлы и проверки.
> Противоречие требований — эскалация к @software-architect, а не самостоятельное решение.

## 0. Инварианты (из ADR-029 — обязательны, не обсуждаются)

1. **Предикат (D1):** «подтверждён» ⟺ у пользователя существует device с непустым `SessionId`
   **или** `RefreshToken`. Вычисляет **backend** (in-memory, через `device.Service`), доставляет
   в бота аддитивным полем gRPC `bool verified = 3` в `message User`. Миграций и backfill нет.
2. **Порядок деплоя: сначала backend, потом bot.** Новый bot + старый backend = всегда
   `verified = false` → keyboard молча не показывается никому (безопасная деградация, `/login`
   и регистрация не затронуты). Логически в декомпозиции backend-задачи (B-1, B-2) тоже идут
   раньше bot'овских, потребляющих поле.
3. **Политика keyboard вычисляется в момент каждой отправки** (lazy, D2), не замораживается
   в конструкторе `Messenger` — иначе ответ `/code` остался бы без keyboard (критерий
   «сразу после подтверждения»).
4. **Кэш `Provider`: хранит только положительных** (`verified == true`); отрицательные состояния
   («не подтверждён» / `not_found` / ошибка gRPC) не кэшируются, перечитываются каждый раз.
   Потокобезопасность `sync.RWMutex` обязательна (доступ из двух горутин: `processMessage`
   и `processNotifications`, D7).
5. **`GetUserId` сохраняет семантику «существование»** — работает и для неподтверждённых
   (регистрационный флоу: `/code` вызывает его **до** подтверждения).
6. **Любая ошибка gRPC внутри `IsAuthorized` → `false`**, обработка сообщения не прерывается
   (edge case «сервис статусов недоступен»).
7. **Платформенное ограничение (осознанно):** у сообщения ровно один `reply_markup` — успешный
   ответ `/login` содержит inline-кнопку и НЕ несёт reply-keyboard; keyboard остаётся видимым
   от предыдущих сообщений. Это не нарушение критерия «к каждому ответу» (ADR-029, D5).
8. **Команда `/login` сама авторизацию не проверяет** — единственный источник истины «может
   войти» = Analytics со своим `404` (ADR 003).
9. **Успех `/code` = отсутствие gRPC-ошибки** (backend'овский `ErrorResponse{Error:"TODO"}`
   бот отбрасывает — известный долг вне рамок задачи, D6).
10. **Вне области:** изменения схем MongoDB; Analytics и nginx; keyboard-меню доп. команд
    (`/get`, `/report`); `ReplyKeyboardRemove` при отвязке; обработка `CallbackQuery`;
    локализация прочих ответов; персонализация `/start`; новых env-переменных нет.

## 1. Карта изменений по файлам

| Файл | Действие | Задача |
|---|---|---|
| `api/inside/accounts.proto` | `message User` + `bool verified = 3;` | B-1 |
| `api/inside/accounts.pb.go` | перегенерация | B-1 |
| `backend/users/processor.go` | чистые хелперы предиката + `verified` в `GetUser`/`GetUsers` | B-2 |
| `backend/users/processor_test.go` | **новый** — тесты хелперов предиката | B-2 |
| `bot/backend/contract.go` | `User` + поле `Verified bool` | BOT-1 |
| `bot/backend/grpc.go` | `GetUser`/`GetUsers` транслируют `v.Verified` | BOT-1 |
| `bot/backend/user/provider.go` | реконструкция кэша: `entry{userId, verified}`, `IsAuthorized`, `sync.RWMutex`, seam `grpcUserClient` | BOT-2 |
| `bot/backend/user/provider_test.go` | **новый** — `IsAuthorized`, политика кэша, `-race` | BOT-2 |
| `bot/commands/keyboard.go` | **новый** — константы текстов + `BuildLoginKeyboard` + `BuildLoginLinkMessage` | BOT-3 |
| `bot/commands/keyboard_test.go` | **новый** | BOT-3 |
| `bot/commands/messenger.go` | **новый** — `Authorizator`, `apiSender`, `Messenger` | BOT-4 |
| `bot/commands/messenger_test.go` | **новый** | BOT-4 |
| `bot/commands/common.go` | `Command.Execute` → `*Messenger`; удалить `sendTextMessage`/`replyToMessage` | BOT-5 |
| `bot/commands/{start,register,get_report,add_receipt,empty,wrong}.go` | миграция на `m.SendText`/`m.ReplyText`/`m.SendDocument` | BOT-5 |
| `bot/commands/{code,login}.go` | только механическая смена сигнатуры `Execute` | BOT-5 |
| `bot/bot.go` | `start`/`processUpdates`/`processMessage` принимают `Authorizator`; `Messenger` на апдейт; `processNotifications` через `Messenger` (D7) | BOT-5 |
| `bot/main.go` | проброс `&provider` в `start(...)` | BOT-5 |
| `bot/commands/login.go` | `Accepted` + текст кнопки; inline-ответ; seam `LoginLinkProvider` (D4, D5) | BOT-6 |
| `bot/commands/login_test.go` | **новый** | BOT-6 |
| `bot/commands/code.go` | ответ «успех/ошибка» (D6) | BOT-7 |
| `bot/commands/registrar.go` | правок **не требует** (проверить) | BOT-5 |

## 2. Задачи

### B-1. gRPC-контракт: поле `User.verified` (proto + перегенерация)

**Файлы:** `api/inside/accounts.proto`, `api/inside/accounts.pb.go`

1. В `message User` (`accounts.proto:19-22`) добавить `bool verified = 3;` (proto3-аддитивность:
   старый потребитель игнорирует поле; новый потребитель старого backend'а получает `false` —
   безопасное направление).
2. Перегенерировать `accounts.pb.go`. Команда генерации — в шапке `contracts.proto`:
   `protoc -I ./ ./*.proto --go_out=. --go_opt=paths=source_relative --go-grpc_out=. --go-grpc_opt=paths=source_relative`.
   Существующие файлы генерированы `protoc 3.14.0` / `protoc-gen-go v1.25.0-devel` — по
возможности использовать тот же toolchain; если точная версия недоступна, перегенерировать так, чтобы diff
   свёлся к новому полю (не «перемалывать» весь файл), и зафиксировать использованные версии
   в описании PR.

**Критерии приёмки:**
- `cd backend && go build ./... && go test ./...` — зелёные;
- `cd bot && go build ./... && go test ./...` — зелёные (бот пока поле игнорирует);
- diff `accounts.pb.go` не содержит изменений в других сообщениях/services.

**Зависимости:** нет. **Приоритет:** P0. **Оценка:** 2 ч.
**Блокит:** B-2, BOT-1 (и всю bot-линейку через них).

---

### B-2. Backend: вычисление предиката «подтверждён» в `users.Processor`

**Файлы:** `backend/users/processor.go`, `backend/users/processor_test.go` (новый)

1. Чистые хелперы (единственный источник предиката, тестируются без I/O):
   - `isDeviceConfirmed(d *nalogDevice.Device) bool` — `d != nil && (d.SessionId != "" || d.RefreshToken != "")`;
   - `confirmedUserIds(devices []*nalogDevice.Device) map[string]bool` — множество `UserId`
     подтверждённых устройств (несколько device у одного пользователя — достаточно любого).
2. `GetUser` (`processor.go:52-64`): `verified = isDeviceConfirmed(...)`, где device —
   `deviceService.GetByUserId(ctx, user.Id.Hex())` (`deviceService` уже внедрён, `processor.go:15`).
   Ошибку `GetByUserId` не трактовать как отказ `GetUser`: лог + `verified = false`
   (в текущем `device.Service` ошибка практически недостижима; деградация того же класса,
   что «сбой = нет keyboard»).
3. `GetUsers` (`processor.go:31-49`): множество один раз из `confirmedUserIds(deviceService.All(ctx))`
   — in-memory, без обращений к Mongo; проставить `Verified` каждому `api.User`.
4. Тесты (`processor_test.go`): `isDeviceConfirmed` — `nil` / пустые токены → `false`; непустой
   `SessionId` или непустой `RefreshToken` → `true`; `confirmedUserIds` — множество по списку
   device, включая «несколько device у одного пользователя» и «у одного из device есть токены».

**Критерии приёмки:** `cd backend && go test ./...` — зелёные; новых обращений к Mongo в
`GetUsers`/`GetUser` нет (только `device.Service`).

**Зависимости:** B-1. **Приоритет:** P0. **Оценка:** 0.5 дн.

---

### BOT-1. Bot: транспорт поля `Verified`

**Файлы:** `bot/backend/contract.go`, `bot/backend/grpc.go`

1. В `User` (`contract.go:13-16`) добавить `Verified bool`.
2. `GrpcClient.GetUser` (`grpc.go:71-93`) и `GrpcClient.GetUsers` (`grpc.go:51-68`) —
   транслировать `Verified: v.Verified` / `Verified: resp.User.Verified`.

**Критерии приёмки:** `cd bot && go build ./... && go test ./...` — зелёные.

**Зависимости:** B-1. **Приоритет:** P0. **Оценка:** 1–2 ч.

---

### BOT-2. Bot: реконструкция кэша `user.Provider` — `IsAuthorized` + потокобезопасность

**Файлы:** `bot/backend/user/provider.go`, `bot/backend/user/provider_test.go` (новый)

1. Seam (D8): неэкспортируемый интерфейс
   `grpcUserClient { GetUser(ctx context.Context, telegramID int) (*backend.User, error) }`
   (его реализует `*backend.GrpcClient`). Поле `Provider.grpc` — этого типа.
2. Реконструкция кэша: map `telegramId → entry{userId string; verified bool}`.
   **В map живут только подтверждённые** (`verified == true`):
   - конструктор `New(grpc *backend.GrpcClient)` — сигнатура прежняя (`main.go` не меняется),
     предзаполнение из `GetUsers` пишет в map только `u.Verified == true`;
   - `GetUserId`: hit → `userId`; miss → gRPC `GetUser`, вернуть `userId` **независимо от**
     `Verified` (семантика «существование» для регистрационного флоу `/code`), в кэш писать
     только при `Verified == true`; ошибка — вернуть как сейчас.
   - новый метод `IsAuthorized(telegramID int) bool`:
     - hit (RLock) → `true` без RPC;
     - miss → **без удержания блокировки** gRPC `GetUser`: `Verified` → дописать в map (Lock) и
       `true`; неподтверждён / `not_found` / любая ошибка → `false` **без кэша**, без паники.
3. `sync.RWMutex` в `Provider`: read-lock на hit, write-lock на дописывание; блокировка не
   удерживается во время gRPC-вызова (доступ из `processMessage` и `processNotifications`).
4. Тесты (`provider_test.go`, fake `grpcUserClient`; конструировать `Provider` в пакете
   напрямую, минуя `New`):
   - hit map (verified) → `true` без RPC (счётчик вызовов fake);
   - miss + `Verified: true` → `true`, второй вызов — без RPC (кэш сработал);
   - miss + найден, `Verified: false` → `false`, второй вызов — **снова RPC** (отрицательное
     не кэшируется);
   - `not_found` / ошибка → `false`, без паники, без кэша;
   - `GetUserId` неподтверждённого → возвращает `userId` (семантика сохранена) и не пишет кэш;
   - конкурентный тест: параллельные `IsAuthorized`/`GetUserId` по одному telegramId
     (имитация двух горутин D7).

**Критерии приёмки:** `cd bot && go test ./...` — зелёные; `go test -race ./backend/user/...` —
без data race.

**Зависимости:** BOT-1. **Приоритет:** P0. **Оценка:** 1 дн.

---

### BOT-3. Bot: `keyboard.go` — константы и конструкторы сообщений

**Файлы:** `bot/commands/keyboard.go` (новый), `bot/commands/keyboard_test.go` (новый)

1. Константы (единственное место жизни текстов):
   ```go
   const (
       LoginButtonText     = "Войти в webUI" // reply-keyboard, нажатие == /login
       OpenWebUiButtonText = "Открыть webUI" // inline URL-кнопка ответа /login
   )
   ```
2. `BuildLoginKeyboard() tgbotapi.ReplyKeyboardMarkup` — один ряд × одна кнопка
   `LoginButtonText`, `ResizeKeyboard: true`, `OneTimeKeyboard: false`, `Selective: false`
   (поля `is_persistent` в tgbotapi v4.6.4 нет; «постоянство» — повторная отправка markup).
3. `BuildLoginLinkMessage(chatID int64, link string) tgbotapi.Message` — текст-подсказка про
   одноразовость/срок ссылки + `InlineKeyboardMarkup` из одной
   `NewInlineKeyboardButtonURL(OpenWebUiButtonText, link)`.
4. Оба — чистые функции, без `bot.Send`.

**Тесты (декларативно, без сети):** `BuildLoginKeyboard` — 1 ряд × 1 кнопка, текст,
`ResizeKeyboard`, `!OneTimeKeyboard`; `BuildLoginLinkMessage` — inline-кнопка
`OpenWebUiButtonText`, `URL == link`.

**Критерии приёмки:** `cd bot && go test ./...` — зелёные.

**Зависимости:** нет (зависит только от tgbotapi) — можно выполнять параллельно с B-*/BOT-1/BOT-2.
**Приоритет:** P0. **Оценка:** 2 ч.

---

### BOT-4. Bot: `commands.Messenger` — единая отправка с политикой keyboard

**Файлы:** `bot/commands/messenger.go` (новый), `bot/commands/messenger_test.go` (новый)

1. Контракты (D2, D8):
   ```go
   // Authorizator decides whether a telegram user gets the quick-access keyboard.
   type Authorizator interface { IsAuthorized(telegramID int) bool }

   // apiSender — seam для тестов; *tgbotapi.BotAPI её удовлетворяет.
   type apiSender interface { Send(config tgbotapi.Chattable) (tgbotapi.Message, error) }

   type Messenger struct {
       api        apiSender
       auth       Authorizator
       chatID     int64
       telegramID int
   }

   func NewMessenger(api apiSender, auth Authorizator, chatID int64, telegramID int) *Messenger
   func (m *Messenger) Send(msg tgbotapi.Message) (tgbotapi.Message, error)
   func (m *Messenger) SendText(text string) (tgbotapi.Message, error)
   func (m *Messenger) ReplyText(text string, replyToMessageID int) (tgbotapi.Message, error)
   func (m *Messenger) SendDocument(doc tgbotapi.DocumentMessage) (tgbotapi.Message, error)
   ```
2. Политика — **в момент каждой отправки** (lazy): `SendText`/`ReplyText`/`SendDocument` при
   `auth.IsAuthorized(telegramID)` кладут `BuildLoginKeyboard()` в `ReplyMarkup` и отправляют
   через `api`; иначе `ReplyMarkup == nil`. `Send` — «сырой»: ничего не подменяет (для ответа
   `/login` с inline-кнопкой, D5).

**Тесты (`messenger_test.go`, fake `apiSender` + fake `Authorizator`):**
- authorized → у отправленного `Chattable` `ReplyMarkup == BuildLoginKeyboard()`
  (сравнение структуры клавиатуры);
- unauthorized → `ReplyMarkup == nil`;
- lazy-семантика: один и тот же `Messenger` реагирует на смену состояния mid-life (fake
  `Authorizator` с переключаемым флагом и счётчиком вызовов — имитация «подтвердили между
  сообщениями»); вызов `IsAuthorized` происходит на каждую отправку.

**Критерии приёмки:** `cd bot && go test ./...` — зелёные.

**Зависимости:** BOT-3. Тип `*user.Provider` удовлетворяет `Authorizator` после BOT-2
(проверяется на этапе BOT-5; здесь — только fake'и).
**Приоритет:** P0. **Оценка:** 0.5 дн.

---

### BOT-5. Bot: смена контракта `Command.Execute` + миграция 8 команд + провязка

**Файлы:** `bot/commands/common.go`, все команды (`start.go`, `register.go`, `code.go`,
`login.go`, `get_report.go`, `add_receipt.go`, `empty.go`, `wrong.go`), `bot/bot.go`,
`bot/main.go`; проверить `bot/commands/registrar.go` (правок не требует).

1. Контракт (D2):
   ```go
   type Command interface {
       Accepted(message string) bool
       Execute(update tgbotapi.Update, m *Messenger) error
   }
   ```
2. Удалить из `common.go` хелперы `sendTextMessage`/`replyToMessage` (становятся методами
   `Messenger`; «тонкие прокси» запрещены — оставили бы путь отправки без keyboard).
   `getContext()` остаётся.
3. Механическая миграция всех 8 команд:
   `sendTextMessage(chatID, bot, ...)` → `m.SendText(...)`;
   `replyToMessage(...)` → `m.ReplyText(...)`;
   `bot.Send(upload)` в `get_report.go:47` → `m.SendDocument(upload)`.
   `code.go` и `login.go` — **только сигнатура/вызовы**, их поведенческие изменения — BOT-6/BOT-7.
4. Провязка (`bot/bot.go`, D7):
   - `processUpdates`/`processMessage` принимают `commands.Authorizator`; `Messenger`
     конструируется **на каждый апдейт**:
     `commands.NewMessenger(bot, auth, update.Message.Chat.ID, update.Message.From.ID)`;
   - `processNotifications(bot, auth, notifications)`: отчёт отправляется
     `commands.NewMessenger(bot, auth, r.TelegramId, r.TelegramId).SendText(r.Message)`;
   - `start(options, reportsClient, registrar, auth)` — расширяется параметром.
5. `bot/main.go`: в `start(...)` передать `&provider` (уже создаётся там, тот же `user.Provider`,
   реализующий `Authorizator` методом `IsAuthorized`). `createCommandsRegistrar` не меняется.

**Критерии приёмки:**
- `cd bot && go build ./... && go test ./...` — зелёные (существующие `register_test.go`,
  `code_test.go` проверяют только `Accepted`/извлекающие функции и не ломаются);
- поведение незарегистрированного пользователя не изменилось (тексты команд те же,
  `IsAuthorized` = false → keyboard нет);
- ни одна команда больше не получает `*tgbotapi.BotAPI`.

**Зависимости:** BOT-2, BOT-4. **Приоритет:** P0. **Оценка:** 1 дн.

---

### BOT-6. Bot: `/login` — распознавание кнопки + inline-ответ

**Файлы:** `bot/commands/login.go`, `bot/commands/login_test.go` (новый)

1. D4 — распознавание нажатия:
   ```go
   func (c *GetLoginLinkCommand) Accepted(message string) bool {
       return message == "/login" || message == LoginButtonText
   }
   ```
   Registrar/порядок не меняются (точные тексты, конфликтов с regex-командами нет).
2. D8 — seam: `LoginLinkProvider { GetLoginLink(ctx context.Context, telegramID int) (string, error) }`
   (реализует `*analytics.Client`); поле команды — интерфейс, `NewGetLoginLinkCommand` совместима
   с вызовом из `main.go`.
3. D5 — ответ:
   - успех: `BuildLoginLinkMessage(chatID, link)` → `m.Send(msg)` **без** reply-keyboard
     (один `reply_markup` на сообщение — инвариант 0.7);
   - ошибка: `m.SendText(err.Error())` — reply-keyboard прикладывается по политике (edge case
     «ошибка получения ссылки — keyboard остаётся»);
   - команда НЕ проверяет авторизацию сама (инвариант 0.8, честный `404` Analytics);
   - повторные нажатия: новое состояние не требуется — каждый раз новый `GetLoginLink`.

**Тесты (`login_test.go`):**
- `Accepted`: `"/login"` → true, `"Войти в webUI"` → true; `"/login "` (с хвостовым пробелом —
  до `TrimSpace` в `processMessage`), `"войти в webui"` (регистр), `"Открыть webUI"`, случайный
  текст → false;
- `Execute` (fake `LoginLinkProvider` + fake `apiSender` через `Messenger`): успех → сообщение
  с inline-URL-кнопкой `OpenWebUiButtonText` и `URL == link`, reply-keyboard нет; ошибка +
  authorized → текст ошибки + `ReplyMarkup == BuildLoginKeyboard()`; ошибка + unauthorized →
  текст, `ReplyMarkup == nil`.

**Критерии приёмки:** `cd bot && go test ./...` — зелёные.

**Зависимости:** BOT-3, BOT-5. **Приоритет:** P0. **Оценка:** 0.5 дн.

---

### BOT-7. Bot: `/code` — ответ пользователю (начинает отвечать)

**Файлы:** `bot/commands/code.go` (+ расширение `code_test.go` — желательно)

1. D6 — `Execute` (после миграции BOT-5):
   - `VerifyPhone` вернул `err == nil` → `m.ReplyText(<сообщение об успешном подтверждении>)`;
     keyboard прикладывается к **этому же** сообщению: `Messenger` lazy (инвариант 0.3), backend
     к моменту ответа уже записал токены (read-after-write в процессе backend'а). Команда НЕ
     связывается с `Provider` ради «маркировки» подтверждения;
   - ошибка → `m.ReplyText(err.Error())`; keyboard по общей политике (на этом этапе
     пользователь обычно ещё не подтверждён → keyboard нет);
   - семантика успеха: отсутствие gRPC-ошибки (`ErrorResponse{Error:"TODO"}` отбрасывается
     `GrpcClient.VerifyPhone` — известный долг backend'а, не трогать);
   - текст ответа — в текущем стиле текстов бота (локализация вне области, ADR-029).

**Тесты (по возможности):** fake-сценарии `Execute` (success → отправлено reply-сообщение;
failure → текст ошибки), через seams из BOT-4.

**Критерии приёмки:** `cd bot && go test ./...` — зелёные; после успешного `/code` пользователь
видит сообщение (раньше команда молчала).

**Зависимости:** BOT-5. **Приоритет:** P0. **Оценка:** 2 ч.

---

### BOT-8. Финальная проверка, ручная приёмка, порядок деплоя

**Зависимости:** B-2, BOT-5, BOT-6, BOT-7. **Приоритет:** P0. **Оценка:** 0.5 дн.

1. Автотесты целиком:
   ```bash
   cd backend && go test ./...
   cd ../bot  && go test ./...
   cd ../bot  && go test -race ./...   # минимум — ./backend/user/... (инвариант 0.4)
   ```
2. Ручная приёмка в Telegram по сценарию задачи (все чекбоксы критериев):
   - `/register +7...` → keyboard НЕТ (зарегистрирован, но не подтверждён);
   - `/code <код>` → ответ с подтверждением **сразу с keyboard** «Войти в webUI»;
   - каждый последующий ответ (успех и ошибка, включая неверную команду) — с keyboard;
   - нажатие «Войти в webUI» → сообщение с inline-кнопкой «Открыть webUI» → переход в webUI;
   - ручной `/login` работает как раньше; повторное нажатие → новая одноразовая ссылка;
   - незарегистрированный: keyboard нет, поведение не изменилось, `/login` — прежний отказ
     по смыслу;
   - Analytics недоступен → текст ошибки, keyboard остаётся.
3. Деплой (инвариант 0.2): образ backend → перезапуск backend → образ bot → перезапуск bot.
   Откат бота на старую версию безопасен (поле игнорируется).

## 3. Зависимости, треки и критический путь

```text
B-1 ──┬─→ B-2 ─────────────────────────────┐
      └─→ BOT-1 ─→ BOT-2 ──┐               │
                           ├─→ BOT-5 ─┬─→ BOT-6 ─┐
BOT-3 ─→ BOT-4 ────────────┘          └─→ BOT-7 ─┴─→ BOT-8
```

| Задача | Зависимости | Оценка |
|---|---|---|
| B-1  | — | 2 ч |
| B-2  | B-1 | 0.5 дн |
| BOT-1 | B-1 | 1–2 ч |
| BOT-2 | BOT-1 | 1 дн |
| BOT-3 | — | 2 ч |
| BOT-4 | BOT-3 | 0.5 дн |
| BOT-5 | BOT-2, BOT-4 | 1 дн |
| BOT-6 | BOT-3, BOT-5 | 0.5 дн |
| BOT-7 | BOT-5 | 2 ч |
| BOT-8 | B-2, BOT-5, BOT-6, BOT-7 | 0.5 дн |

**Итого:** ≈ 4.5–5 человеко-дней.

**Параллельные треки:**
- Трек backend: B-1 → B-2 (один разработчик, старт сразу);
- Трек bot-инфраструктура: BOT-1 (после B-1) → BOT-2;
- Трек UI-компонентов: BOT-3 → BOT-4 (не ждёт backend, старт сразу);
- Сходится в BOT-5 (нужны BOT-2 и BOT-4), далее BOT-6 ∥ BOT-7, финиш BOT-8.

**Критический путь:** B-1 → BOT-1 → BOT-2 → BOT-5 → BOT-6 → BOT-8 ≈ 3 рабочих дня.
При одном разработчике безопасный порядок — по таблице (B-1, B-2, BOT-3, BOT-1, BOT-4, BOT-2,
BOT-5, BOT-6, BOT-7, BOT-8).

## 4. Соответствие критериям приёмки задачи

| Критерий задачи | Задача(и) |
|---|---|
| Keyboard авторизованному (зарегистрирован + подтверждён) | B-1, B-2, BOT-1, BOT-2, BOT-4, BOT-5 |
| Keyboard к каждому ответу, включая ошибки | BOT-4, BOT-5 (в т.ч. `processNotifications`) |
| Keyboard сразу после подтверждения | BOT-4 (lazy) + BOT-7 |
| Незарегистрированный не видит keyboard, поведение не меняется | BOT-2 (политика кэша), BOT-5 |
| Нажатие кнопки == `/login`; ручной `/login` работает | BOT-6 |
| Ответ со ссылкой — inline-кнопка «Открыть webUI» | BOT-3, BOT-6 |
| Юнит-тесты (по таблице ADR «Тестование», `-race`) | B-2, BOT-2, BOT-3, BOT-4, BOT-6, BOT-8 |
| `cd bot && go test ./...` зелёные | все, финализация BOT-8 |
| Ручная проверка в Telegram | BOT-8 |

## 5. Замечания для реализации

- **Не менять:** `registrar.go` (порядок first-wins сохранён, `/login` зарегистрирован раньше
  default), контракты Analytics HTTP, `analytics.Client.GetLoginLink`, `bot/backend/client.go`,
  механику ADR 004 (`WaitForReady`, предзаполнение `Provider` — предзаполнение теперь пишет
  только подтверждённых).
- **Тексты кнопок — только константы из `keyboard.go`**; нигде более литералов кнопок.
- `SendDocument`: `tgbotapi.DocumentMessage` наследует `ReplyMarkup` через `BaseMessage`
  (tgbotapi v4.6.4) — подмена markup возможна без изменения типа.
- Если при перегенерации proto toolchain'ом недоступен — эскалация, не «ручное редактирование»
  `accounts.pb.go`.
- Любое расхождение с ADR-029 (рев. 2) — стоп и эскалация к @software-architect.
