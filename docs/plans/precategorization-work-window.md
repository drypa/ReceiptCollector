# План: рабочее окно фоновой предкатегоризации товаров

**Связанный ADR:** [ADR-030](../adr/030-precategorization-work-window.md)
**Основание:** требование заказчика от 2026-10-26 — задать в конфиге время старта и окончания
(наиболее свободное время); только внутри окна проверять активность и запускать ИИ-категоризацию.

## Область изменений

Только .NET Analytics: опции `Precategorization`, фоновый сервис, конфигурация и документация.
Go-сервисы, nginx, БД, API и webUI — без изменений. Поведение по умолчанию в коде сохранено:
без заданных границ фон работает круглосуточно, как до ADR-030.

## Что меняется

### 1. `Infrastructure/Configuration/Options/PrecategorizationOptions.cs`

- `WorkWindowStart: TimeSpan?`, `WorkWindowEnd: TimeSpan?` — время суток (формат `TimeSpan`,
  например `"02:00:00"`), по умолчанию `null`.
- `WorkWindowTimeZone: string?` — IANA/Windows id, по умолчанию `null` (локальное время сервера).
- Окно задаётся обеими границами или не задаётся вовсе.

### 2. `Infrastructure/Configuration/DependencyInjectionExtensions.cs`

`PrecategorizationOptions` получает `ValidateOnStart`-проверки:

- только одна граница окна;
- граница вне `[00:00:00, 24:00:00)`;
- `WorkWindowStart == WorkWindowEnd`;
- невалидный `WorkWindowTimeZone`.

### 3. `Infrastructure/Modules/Commodities/PrecategorizationWorkWindow.cs` (новый)

Чистая арифметика окна: `IsConfigured`, `IsOpen` (полуинтервал `[Start, End)`, переход через
полночь, нулевое окно закрыто), `Describe` для стартового лога.

### 4. `Infrastructure/Modules/Commodities/PrecategorizationBackgroundService.cs`

- Часовой пояс разрешается один раз в конструкторе (`ResolveWorkWindowTimeZone`), время берётся
  через `TimeProvider` (`GetLocalNow` либо `ConvertTime` из UTC).
- В цикле: `IsIdle` вызывается только если окно открыто; вне окна — `Debug`-сообщение.
- В сессии: проверка окна перед каждой позицией и после обработки текущей; при закрытии —
  `stopReason = "window_closed"` без отката уже сохранённых строк.
- Стартовый лог содержит описание окна.

### 5. Конфигурация

- `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` — `02:00:00`–`06:00:00`,
  `Europe/Moscow`.
- `docker-compose.yml` (сервис `analytics`) — `Precategorization__WorkWindow*` с `:-`-дефолтами.
- `.env.example` — закомментированный блок с пояснением семантики и часового пояса.
- `AGENTS.md` — три строки в таблице `Precategorization__*`; ADR-027 — amend-заметка на ADR-030.

### 6. Тесты

- `PrecategorizationOptionsTests` — дефолты `null`, биндинг окна, ошибки старта (частичное окно,
  границы вне суток, равные границы, невалидный пояс).
- `PrecategorizationBackgroundServiceTests` — вне окна сессия не стартует; окно открывается по
  времени и сессия стартует; сессия останавливается после текущей позиции при закрытии окна;
  часовой пояс окна применяется (`Europe/Moscow`).
- `PrecategorizationWorkWindowTests` (новый) — границы, конец не включается, окно через полночь,
  незаданное и нулевое окно, описание для лога.

## Проверка

```bash
cd Analytics
dotnet test tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj \
  --filter "FullyQualifiedName~Precategorization"
dotnet test   # полный прогон
```

Ручная проверка (dev):

1. `Precategorization__WorkWindowStart/End` заведомо вне текущего времени → в логах нет
   `session started`, есть только стартовый лог с окном.
2. Границы вокруг текущего времени, простой ≥ `IdleTimeout` → сессия запускается; при переходе
   через конец окна в логе `Reason: window_closed`, обработанные строки в
   `precategorized_commodities` сохранены.
3. Окно через полночь (`23:00:00`–`06:00:00`) — сессия запускается в 23:30 и в 01:00.
4. `WorkWindowTimeZone=Europe/Moscow` при UTC-контейнере — окно срабатывает по Москве.

## Риски и откат

- Дефолт `02:00–06:00 Europe/Moscow` активен в `appsettings.json` и `docker-compose.yml`; при
  необходимости окно отключается удалением обеих границ (или env-переопределением).
- Откат: убрать три параметра из конфигурации — сервис вернётся к круглосуточной проверке простоя;
  код устойчив к отсутствию границ.
