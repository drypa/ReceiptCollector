# Задача 05 - Автоприменение дефолта при импорте чека

- **Задача**: docs/plans/merchant-default-commodity-category.md#t-05-автоприменение-дефолта-при-импорте-чека
- **Дата**: 2026-11-05
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

`ReceiptSynchronizationService` (единственная точка импорта чеков MongoDB → PostgreSQL) получил
блок автоприменения дефолтной товарной категории магазина «при поступлении» (ADR-032, C1/FR-M2):
сразу после успешного `AddAsync(receipt)` берётся `merchant.DefaultCommodityCategory` уже
резолвленного `ResolveMerchantAsync` магазина (нового чтения merchant нет). Дефолт не задан
(включая `null` безопасного чтения невалидного кода из T-02) — немедленный выход с нулевым
overhead, существующий поток импорта не меняется (FR-M8). Иначе вызывается
`ApplyMerchantDefaultForReceiptAsync(receipt.Id, category)` из T-04 (своя транзакция, позиции
только без категории, `category_source = 'merchant-default'`, `TryAdd` в кэш после commit),
число обновлённых строк логируется. Сбой операции логируется Error и не валит цикл синхронизации:
чек уже закоммичен, его позиции без категории дожмут кнопкой (T-06/T-10); отмена токена
(graceful shutdown) пробрасывается без ложного Error. Чек без привязки к merchant — прежнее
правило пропуска, не тронуто.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/ReceiptSynchronizationService.cs` —
  зависимость `ICommodityRepository` (конструктор + null-check); вызов нового приватного
  `ApplyMerchantDefaultCategoryAsync` после `AddAsync`/`imported++`; метод: no-op при `null`
  дефолте, `LogInformation` с числом обновлённых позиций, `catch (OperationCanceledException)
  when (токен отменён) → throw` перед общим `catch (Exception)` + `LogError` и продолжением
  импорта (ADR-032 п. 3). DI-правок не потребовалось: `ICommodityRepository` уже Scoped,
  сервис резолвится автоваейрингом в скоупе хостед-сервиса.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/ReceiptSynchronizationServiceTests.cs` —
  расширение существующего класса (4 новых теста, Testcontainers Mongo+PG, NSubstitute):
  магазин с дефолтом → применение вызвано ровно 1 раз с парой (id импортированного чека из БД,
  категория дефолта); магазин без дефолта → чек импортирован, применение не вызывалось
  (`DidNotReceiveWithAnyArgs`); сбой применения на обоих чеках → `SynchronizeAsync` не бросает,
  оба чека в БД, 2 вызова применения, ровно 2 Error-записи с исключением; `AddAsync` бросает →
  применение не вызвано (блок привязан строго к успешному сохранению). Хелперы:
  `SeedMerchantAsync` (магазин по INN тикета с/без дефолта), `RecordingLogger<T>` (по образцу
  `TestLogger` из `MongoReceiptBatchLoaderTests`); `CreateService` — опциональные параметры
  `ICommodityRepository`/`IReceiptRepository`/`ILogger` (тела существующих тестов не изменены).
- `docs/plans/merchant-default-commodity-category.md` — чек-лист T-05, статус строки 05.

## Как проверено

- `cd Analytics && dotnet build` — 0 ошибок.
- `cd Analytics && dotnet test --filter "FullyQualifiedName~ReceiptSynchronizationService"` —
  **11 passed / 11** (7 существующих + 4 новых), без предупреждений компиляции в тестовом классе.
- `cd Analytics && dotnet test` — **408 passed / 408** (baseline 404 + 4 новых, без правок
  существующих тестов).

## Итоги ревью

- APPROVED в 1 раунд, blockers нет.
- Исправлено по неблокирующим замечаниям ревьюера:
  - `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`
    перед общим catch в блоке применения — при graceful shutdown в лог не попадает ложный
    «Failed to apply merchant default»; тест «сбой применения → импорт продолжается» зелёный
    (его мок бросает обычный `InvalidOperationException`, не отмену).
  - Добавлен тест порядка `AddAsync` → применение: `AddAsync` бросает — применение дефолта не
    вызвано ни разу, чек в БД отсутствует, цикл синхронизации переживает сбой сохранения.
  - Перевод строки в конце файла тестов.
- Оставлено без изменений (с уточнением): буквальная реализация ADR-032 п. 3
  («catch + LogError + продолжение импорта») — широкий catch для всех сбоев применения, кроме
  целенаправленной отмены. Уточнение: если отмена приходит в другой код того же чека, возможен
  один общий Error «Failed to import receipt» — это существующее поведение импорта (outer catch),
  вне рамок T-05.
- Реализационные детали (прецеденты вместо неподходящего API): NSubstitute `ThrowsAsync/Throws`
  не компилируется для `Task`/`Task<int>` в данной конфигурации — использованы
  `Returns<Task<int>>(Task.FromException<int>(...))` (прецедент `ReceiptCategorizationEndpointsTests`)
  и `.When(...).Do(...)` (прецедент `CommodityRepositoryMerchantDefaultTests`); проверки
  `DidNotReceiveWithAnyArgs` для async-методов — через `await` (прецедент
  `PrecategorizedCommodityEndpointsTests`).

## Заметки для коммита

- Рекомендуемое сообщение: `T-05: автоприменение дефолтной категории магазина при импорте чека в ReceiptSynchronizationService (ADR-032 C1/FR-M2)`.
- **НЕ включать** `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
  `appsettings.Development.json` — локальные dev-настройки (AI-endpoint `127.0.0.1:1234`,
  TelegramId `136871539`, mongo-креды, `Skip:false`), к задаче отношения не имеют.
- Ручная проверка сквозного сценария (новый чек магазина с дефолтом → позиции категоризованы,
  подпись источника «по умолчанию магазина») — в T-11 на dev-стенде; здесь поведение покрыто
  интеграционными тестами сервиса синхронизации (реальные PG/Mongo контейнеры).
- Поведение по умолчанию (без дефолта) не изменилось: все 7 существующих тестов синхронизации
  прошли без правок; конфигурация и env-переменные не расширялись (ADR-032: дефолт — данные).
