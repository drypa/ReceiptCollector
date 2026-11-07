# Задача 04 - Точка фиксации 1: сохранение категорий чека

- **Задача**: docs/plans/categorization-decision-log.md#t-04-точка-фиксации-1--сохранение-категорий-чека-put-apireceiptsidcategories
- **Дата**: 2026-11-07
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

`PUT /api/receipts/{id}/categories` теперь принимает аддитивный опциональный снимок предложения
(`suggestedCategory`, `suggestionSource`) в каждом элементе `items`. Если клиент прислал валидную
пару «категория + источник», после успешного сохранения категорий в журнал «предложено → принято»
пишется событие; без снимка поведение и ответы прежние, событий нет. Невалидный или частичный
снимок отбрасывается с `Warning` (только количество) и не даёт `400`. Сброс категории («Не указана»)
после предложения фиксируется как решение с `final = Undefined` (`0`). Принявший берётся из
`UserContext`, флаг «админ» — снимок `users.is_admin`, который эндпоинт резолвит один раз за запрос
и только при наличии хотя бы одного валидного снимка. Запись журнала — после commit пользовательских
сохранений, одним вызовом recorder'а, и не влияет на ответ (NFR-3).

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategorizationService.cs` - `ConfirmedCategoryAssignment` += `SuggestedCategory`/`SuggestionSource` (опциональные, по умолчанию `null`); `ApplyConfirmedCategoriesAsync` += `bool acceptedByAdmin`; XML-док фиксирует best-effort журнал и NFR-3.
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/CategorizationDecisions/Models/CategorizationDecisionModels.cs` - актуализирован XML-док `CategorizationDecisionDraft`: save-путь не фильтрует черновики, recorder по контракту не бросает, save-путь дополнительно страхует NFR-3.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` - в конструктор добавлены `ICategorizationDecisionRecorder` и `ILogger<CommodityCategorizationService>`; после цикла применения собираются черновики по применённым присвоениям с непустым снимком (`FinalCategory = assignment.Category ?? Undefined`), один вызов recorder'а после commit; защитный `catch (Exception)` + `LogError` на случай нарушения контракта recorder'а. `updated`, порядок `UpdateCategoryAsync`/`_cache.TryAdd` и игнор чужих `commodityId` не изменены.
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs` - `SaveCategoryItemRequest` += `SuggestedCategory`/`SuggestionSource`; разбор снимка (нет/валиден/невалиден) без новых кодов ответов; `Warning` с количеством отброшенных снимков; admin-флаг через `IUserRepository.GetByIdAsync` один раз и только при валидном снимке; существующая валидация `items`/`category` и коды ответов сохранены.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategorizationServiceTests.cs` - фабрика обновлена (recorder + `NullLogger`), вызовы получили `acceptedByAdmin`; добавлены тесты: согласие, расхождение, отсутствие снимка, reset -> `FinalCategory = Undefined`, чужой `commodityId`, проброс admin-флага, батч «3 присвоения, 2 со снимком -> один `RecordAsync` с 2 черновиками», порядок «сначала commit, потом журнал», сбой recorder'а (`InvalidOperationException`/`OperationCanceledException`) -> результат сохранения не меняется.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCategorizationEndpointsTests.cs` - вызовы/заглушки обновлены под новую сигнатуру; добавлены тесты: валидный снимок -> поля и admin-флаг доходят до сервиса, ровно один запрос в `users`; невалидный/частичный снимок (7 кейсов, включая `existing`, `Undefined`, число, `AI`) -> `200`, снимок отброшен, `users` не опрашивается; без снимков `users` не опрашивается; `GetByIdAsync -> null` -> `acceptedByAdmin = false`, `200`; reset + снимок пробрасывается.
- `docs/plans/categorization-decision-log.md` - строка статуса T-04: `pending` -> `in-progress` -> `review` -> `approved`.

## Как проверено

- `cd Analytics && dotnet build --no-incremental` - Build succeeded, 0 errors; предупреждения только существующие NU1902/NU1903 по пакетам.
- `cd Analytics && dotnet test --filter "FullyQualifiedName~ReceiptCategorization|FullyQualifiedName~CommodityCategorizationService"` - Passed: 56, Failed: 0.
- `cd Analytics && dotnet test` - Passed: 498, Failed: 0, Skipped: 0 (1 m 4 s). База T-03 - 476 тестов; добавлено 22, существующие утверждения не менялись.
- Ревьюер независимо прогнал `dotnet build` (0 errors) и `dotnet test` (496 passed / 0 failed на момент ревью) и подтвердил аддитивность контрактов, семантику фиксации, порядок «commit -> журнал», обоснование по change tracker и NFR-1.

## Итоги ревью

- **Осознанное отклонение (зафиксировано, чтобы на приёмке T-12 это не выглядело случайностью):** в сервисе вокруг вызова recorder'а стоит защитный `catch (Exception)` + `LogError`. План T-04 говорит «исключений от recorder'а сервис не ждёт и не обрабатывает», но перечень тестов задачи требует «recorder бросает -> сохранение успешно, код ответа прежний», а NFR-3 — «сбой журнала не влияет на ответ». Guard срабатывает только при нарушении контракта recorder'а (штатно recorder не бросает) и усиливает структурную гарантию NFR-3.
- **Вынос `ParseSuggestionSnapshot` в общий helper** — по плану T-05 (тогда правила разбора снимка будут переиспользованы страницей «Товары»). В T-04 helper оставлен приватным в `ReceiptCategorizationEndpoints`, чтобы не выходить за рамки задачи.
- **Двойное логирование Error (suggestion ревьюера).** Оставлено `Error` в сервисе: фактического дублирования нет — при штатном сбое recorder ловит исключение внутри себя, пишет один `Error` и возвращает управление нормально; сервисный guard пишет лог только если recorder нарушил контракт и бросил исключение (тогда собственного лога recorder'а нет). Уровень `Error` отражает тяжесть нарушения контракта, поэтому понижение до `Warning`/`Debug` не применялось.
- **Лог `Warning` о невалидных снимках при последующем `400` (suggestion ревьюера).** Не применено осознанно: запрос отклонён, сохранения и событий нет; предупреждение о снимках отклонённого запроса — шум в логах, а перенос логирования потребовал бы переупорядочить валидацию (сначала собрать все ошибки формы, потом писать Warning), что увеличивает diff и риск изменения кодов ответов. Цена выше пользы.
- **Применённые suggestions ревьюера (дешёвые и безопасные):**
  - тест «3 присвоения, 2 с валидным снимком -> ровно один `RecordAsync` с 2 черновиками»;
  - тест `GetByIdAsync -> null` в эндпоинте (`acceptedByAdmin = false`, `200`, сохранение проходит);
  - актуализирован XML-док `CategorizationDecisionDraft` (save-путь не фильтрует черновики; recorder не бросает; сервис страхует NFR-3).
- **Change tracker (предупреждение из сводки T-03):** решено порядком вызова, без detach. Запись журнала — последняя БД-операция запроса: после `RecordAsync` сервис и эндпоинт ничего не сохраняют, scope `DbContext` завершается; до recorder'а все `UpdateCategoryAsync` уже закоммичены, `IUserRepository` читает `AsNoTracking`. Повторная вставка журнала в том же запросе невозможна; обоснование закреплено комментарием у `RecordDecisionsBestEffortAsync`.
- **Прежние утверждения тестов не менялись:** в существующих тестах обновлены только фабрика, вызовы и подстановки аргументов (`acceptedByAdmin`, `IUserRepository`, `ILoggerFactory`).

## Заметки для коммита

- Рекомендуемое сообщение коммита: `categorization decision log receipt save point`
  (в стиле T-01 `categorization decision log migration`, T-02 `t-02 domain of categories journal`,
  T-03 `T-03 Recorder`).
- Новых untracked-файлов кода нет; единственный новый файл — эта сводка
  (`docs/reviews/categorization-decision-log/04-receipt-save.md`). В коммит включить:
  - `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategorizationService.cs`
  - `Analytics/src/ReceiptCollector.Analytics.Application/Modules/CategorizationDecisions/Models/CategorizationDecisionModels.cs`
  - `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategorizationService.cs`
  - `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs`
  - `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategorizationServiceTests.cs`
  - `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCategorizationEndpointsTests.cs`
  - `docs/plans/categorization-decision-log.md`
  - `docs/reviews/categorization-decision-log/04-receipt-save.md`
- **Не включать** предсуществующие посторонние изменения:
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
  `appsettings.Development.json`, `wwwroot/index.html`.
- На что обратить внимание при ручной проверке:
  1. Сохранение чека без снимков — payload, тело и коды ответов прежние; в `categorization_decisions` записей нет;
     `users` не читается.
  2. Suggest -> сохранить без правок: по позициям с предложением появляются согласия (`suggested = final`);
     после сброса одной позиции — событие с `final = 0`.
  3. Правка категории после предложения — событие с расхождением; невалидный/частичный снимок
     (`existing`, `undefined`, число, неизвестный источник) не даёт `400` и не создаёт событие.
  4. Порядок: событие пишется только после успешного сохранения; при недоступной записи журнала
     сохранение успешно, в логах `Error`.
