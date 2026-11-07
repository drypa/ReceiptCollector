# Задача 06 - Точка фиксации 3: подтверждение в `/precategorized`

- **Задача**: docs/plans/categorization-decision-log.md#t-06-точка-фиксации-3--подтверждение-в-precategorized-решение-d1
- **Дата**: 2026-11-07
- **Ревью**: раундов - 1, вердикт APPROVED (блокеров нет)

## Что сделано

Подтверждение строки очереди в админском разделе `/precategorized` (`POST /api/precategorized-commodities/{id}/confirm`) теперь пишет в журнал «предложено → принято» одно событие на строку очереди (ADR-033 п. 5, решение D1): предложено = неизменяемое исходное предложение строки (`original_proposed_category_id`, читается до транзакции), принято = категория, реально применённая при confirm (после правок `PUT .../category` это исправленное значение — расхождение видно фильтром «только расхождения»). Принявший — авторизованный администратор из `UserContext` (пробрасывается в сервис), флаг «админ» всегда `true`. `NotFound`/`Conflict` и отклонение строки (`DELETE`) событий не дают; при `updatedCommodities = 0` событие пишется — решение состоялось. Запись — строго после commit транзакции подтверждения и пополнения кэша, с защитным `catch` (прецедент T-04/T-05); транзакция, статусы, тело и коды ответов confirm не менялись (NFR-1/NFR-3).

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/IPrecategorizedCommodityService.cs` - `ConfirmAsync` += параметр `Guid acceptedByUserId`; XML-док фиксирует источник значения (`UserContext`), семантику событий по outcome и независимость ответа от журнала.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/PrecategorizedCommodityService.cs` - конструктор += `ICategorizationDecisionRecorder` + `ILogger` (регистрация scoped есть с T-03, DI-файл не менялся); после `Applied` и `_cache.TryAdd` — один черновик (`result.Name` — точное имя строки, источник `precategorized`, `SuggestedCategory = proposal.OriginalProposedCategory`, `FinalCategory` — применённая категория, принявший, admin `true`) и один вызов recorder'а через `RecordDecisionsBestEffortAsync` (защитный `catch` + `LogError`, журнал — последняя БД-операция запроса).
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs` - `Confirm` передаёт `UserContext.UserId!.Value` (после `AdminAuthorization` пользователь гарантирован; комментарий о безопасности `!.Value`).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedCommodityServiceTests.cs` - фабрика `CreateService` под новую сигнатуру (recorder-substitute + `NullLogger`), вызовы с принявшим; новые тесты: одиночный черновик `suggested = исходная`, `final = применённая`, источник/admin/принявший, имя — точное (не обрезано); порядок «commit -> кэш -> журнал» (в колбэке recorder'а проверяется и `cache.Count == 1` — применённая suggestion ревьюера); recorder бросает (`InvalidOperationException`/`OperationCanceledException`) -> результат, кэш и `updatedCommodities` не меняются. Усилены без ослабления: `NotFound`/`Conflict`/`DeclineAsync`/`UpdateCategoryAsync` -> recorder не вызывается, `updated = 0` -> событие есть.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityWorkflowTests.cs` - реальный `CategorizationDecisionRecorder` + `CategorizationDecisionRepository` на том же `ReceiptDbContext` (как DI); `ClearDatabaseAsync` чистит `categorization_decisions`; helper `Proposal` += необязательный `original` (по умолчанию = proposed); три новых Testcontainers-кейса: «`UpdateCategoryAsync` (PUT) -> confirm» -> событие `suggested = Dairy` (исходная), `final = Food` (изменённая) со всеми полями FR-1; «confirm без PUT» -> `suggested == final`; «confirm при 0 обновлённых товарах» -> событие есть; Decline -> журнал пуст.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedCommodityEndpointsTests.cs` - вызовы/заглушки под новую сигнатуру (только подстановки аргументов); новый тест: `Confirm` передаёт в сервис `UserContext.UserId` авторизованного администратора.
- `docs/plans/categorization-decision-log.md` - строка статуса T-06: `pending` -> `in-progress` -> `review` -> `approved`.

## Как проверено

- `cd Analytics && dotnet build` - Build succeeded, 0 errors, 30 warnings (только существующие NU1902/NU1903 по пакетам, были до задачи).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorized"` - Passed: 53, Failed: 0 (20 s).
- `cd Analytics && dotnet test` - Passed: 530, Failed: 0, Skipped: 0 (1 m 01 s до правки по suggestion и 54 s после неё). База T-05 - 522; добавлено 8 кейсов (4 сервиса + 3 workflow + 1 эндпоинт), утверждения существующих тестов не ослаблялись (обновлены только фабрика и вызовы под новую сигнатуру).
- Ревьюер независимо прогнал `dotnet build` (0 errors) и `dotnet test` (530 passed / 0 failed), фильтр `~Precategorized` (53 passed), проверил честность workflow-теста D1 (сид фона, реальный `UpdateCategoryAsync`, чтение события из Postgres вторым контекстом), семантики outcome, NFR-1/NFR-3 и CJK-сканирование изменённых `.cs` (0 вхождений).

## Итоги ревью

- **Принятые отклонения (зафиксированы, чтобы на приёмке T-12 это не выглядело случайностью):**
  - `FinalCategory = result.Category.Value` вместо литерального `proposal.ProposedCategory` из текста плана: значения тождественны по построению (сервис передаёт репозиторию `proposal.ProposedCategory`, репозиторий возвращает применённое); взято возвращённое репозиторием значение как буквально «реально применённое». Закреплено комментарием в коде.
  - `CommodityName = result.Name` без `Trim` — точное имя строки очереди; обрезка внешних пробелов и нормализация — ответственность recorder'а (принятое уточнение T-03). Покрыто тестом с именем «  Молоко   3,2%  ».
  - Хелпер `Proposal` в workflow-тестах пишет `original ?? category` — сиды согласованы с D1 (фон создаёт строку с `original = proposed`); раньше сид писал `original = 0`, что нигде не проверялось. Утверждения существующих тестов не менялись.
  - Защитный `catch (Exception)` + `LogError` вокруг вызова recorder'а — продолжение зафиксированного прецедента T-04/T-05: штатно recorder не бросает, guard страхует NFR-3 только при нарушении контракта; требуется перечнем тестов задачи.
- **Применённая suggestion ревьюера (после вердикта):** тест порядка теперь проверяет и позицию относительно кэша — в колбэке recorder'а захватывается `cache.Count == 1` (журнал идёт после `_cache.TryAdd`). Перезапуск: фильтр `~Precategorized` - 53 passed, `dotnet test` - 530 passed, 0 failed.
- **Заметка на будущее (не к этой задаче):** best-effort helper (`RecordDecisionsBestEffortAsync`) теперь существует в трёх копиях (T-04 `CommodityCategorizationService`, T-05 `CommodityBatchCategorizationService`, T-06 `PrecategorizedCommodityService`). DRY-вынос осознанно отложен до завершения T-12, чтобы не смешать приёмку журнала с рефакторингом; при выносе сохранить семантику «последняя БД-операция запроса» и уровень `Error`.
- **Прежние утверждения тестов не менялись:** в существующих тестах обновлены только фабрика, вызовы и подстановки аргументов (`acceptedByUserId`).

## Заметки для коммита

- Рекомендуемое сообщение коммита: `T-06 precategorized confirm point` (в стиле T-03 `T-03 Recorder`, T-04 `t-04 category safe`, T-05 `t-05 batch processing on commodities page`).
- Чек-лист файлов коммита (проверить по `git status`; кодовых/тестовых - 6, новых untracked нет):
  - `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/IPrecategorizedCommodityService.cs`
  - `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/PrecategorizedCommodityService.cs`
  - `Analytics/src/ReceiptCollector.Analytics.Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs`
  - `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedCommodityServiceTests.cs`
  - `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityWorkflowTests.cs`
  - `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedCommodityEndpointsTests.cs`
  - `docs/plans/categorization-decision-log.md`
  - `docs/reviews/categorization-decision-log/06-precategorized-confirm.md` (новый — эта сводка)
- Внимание по состоянию индекса: на момент финализации сводки все 7 существующих файлов задачи уже добавлены в индекс внешним процессом, staged-версии содержат финальные правки (проверено); сама эта сводка — untracked, её нужно добавить в коммит (`git add`), агенты этого не делают. Перед коммитом сверить итог с `git status`.
- В диффе плана будет также строка T-05 `approved -> committed` (выставлена оркестратором после коммита T-05) — это норма, пусть едет в коммите.
- **Не включать** предсуществующие посторонние изменения: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`, `appsettings.Development.json`, `wwwroot/index.html`.
- На что обратить внимание при ручной проверке:
  1. `/precategorized`: изменить предложение строки (PUT) -> confirm -> в `categorization_decisions` одна строка с `suggestion_source = 'precategorized'`, `suggested_category_id` = исходное предложение, `final_category_id` = исправленное, `accepted_by` = id админа, `accepted_by_admin = true`; фильтр «только расхождения» на странице журнала (когда появится в T-08) её покажет.
  2. Confirm без правки предложения -> событие с `suggested = final`; confirm строки, у которой ни один товар не обновился (`updated_commodities = 0` в ответе) -> событие всё равно есть; отклонение строки -> событий нет.
  3. Ответ `POST .../confirm` — прежние тело и коды (`200/401/403/404/409`); при недоступной записи журнала (например, временно убрать права на INSERT) — confirm успешен, в логах `Error`; категория в товарах и кэш — как раньше, автопроставления нет.
