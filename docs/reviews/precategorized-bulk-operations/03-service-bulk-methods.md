# Задача 03 - Сервис: контракт `ConfirmManyAsync`/`DeclineManyAsync`, кэш и журнал после commit, наблюдаемость

- **Задача**: docs/plans/precategorized-bulk-operations.md#03-сервис-контракт-confirmmanyasyncdeclinmanyasync-кэш-и-журнал-после-commit-наблюдаемость
- **Дата**: 2026-11-13
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Готов сервисный слой массовых операций страницы «Предкатегоризация»: бизнес-логика собрала
контракт задач 01 и 02 в единый результат. `IPrecategorizedCommodityService` аддитивно получила
`ConfirmManyAsync(ids, acceptedByUserId, ct)` и `DeclineManyAsync(ids, ct)`; реализация вызывает
batch-методы репозитория (одна транзакция на список), маппит исходы в wire-модели в порядке
запроса (сопоставление по id) и соблюдает инварианты `applied + skipped = requested` и
`declined + skipped = requested`.

Строго после commit: `cache.TryAdd` по каждой `applied`-строке подтверждения (ADR-019,
first-wins/no-op) и **один** вызов журнала решений всеми draft'ами применённых строк с
`CancellationToken.None` (ADR-033, E1; recorder - последняя БД-операция запроса, его сбой не
меняет счётчики, кэш и ответ). Отклонение кэш и журнал не затрагивает. Сбой репозитория
пробрасывается наружу до блока кэш/журнал - они не вызываются. Пустой `ids` защищённо даёт нули
без обращения к репозиторию.

Подключена наблюдаемость ADR-036 §7: одна `Information`-строка на операцию со счётчиками,
разбивкой пропусков по причинам, длительностью в мс и признаком отмены; `Warning` - все строки
пропущены, прерывание, длительность выше порога 30 с; `Error` - технический сбой. Наименования
товаров и ids в логах сервиса отсутствуют вовсе (строже требования «только `Debug`»). Поштучные
`ConfirmAsync`/`DeclineAsync` не изменены.

## Изменённые файлы

Application:

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/IPrecategorizedCommodityService.cs`
  (аддитивно) - `ConfirmManyAsync(IReadOnlyCollection<Guid>, Guid, ct)` → `BulkConfirmResult` и
  `DeclineManyAsync(IReadOnlyCollection<Guid>, ct)` → `BulkDeclineResult` с XML-doc: одна
  транзакция на список; `NotFound`/`Conflict` → `skipped` с причиной; технический сбой -
  исключение (эндпоинт отдаёт `500`); кэш и журнал - после commit; recorder с
  `CancellationToken.None`; пустой `ids` - нули без репозитория. Существующие сигнатуры не
  изменены.

Infrastructure:

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/PrecategorizedCommodityService.cs`
  - Конструктор пополнился `TimeProvider` (замер длительности, конвенция
    `CommodityBatchCategorizationService`: параметр перед `ILogger`; в DI `TimeProvider.System`
    уже зарегистрирован, регистрация сервиса не менялась).
  - `ConfirmManyAsync`: пустой `ids` → нули; вызов `_repository.ConfirmManyAsync`; после возврата
    (commit выполнен) - `TryAdd(NormalizeName(Name), применённая категория)` и draft'ы
    (`SuggestedCategory = OriginalProposedCategory`, `FinalCategory = ProposedCategory`,
    `AcceptedByAdmin: true`, `AcceptedByUserId` - серверное значение) **только** по `Applied`-
    строкам; один `RecordDecisionsBestEffortAsync(drafts, CancellationToken.None)` через
    существующий best-effort helper (защитный `catch` с `Error` - NFR-3); сборка
    `BulkConfirmResult` в порядке `ids` (индекс `BuildByIdIndex`), `updatedCommodities` - сумма
    по `Applied`.
  - `DeclineManyAsync`: тот же маппинг (`Applied` → `declined`, `Conflict` →
    `skipped/already-applied`, `NotFound` → `skipped/not-found`), кэш и журнал не затрагиваются.
  - Наблюдаемость §7: `Information` на операцию; `Warning` - все пропущены / прерывание
    (`OperationCanceledException` при отменённом токене) / длительность выше
    `BulkSlowOperationThreshold = 30 с` (константа с комментарием, заметно ниже дедлайна
    эндпоинта 90 с); `Error` - прочее исключение с пробросом. Логи без ПД.

Tests:

- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedCommodityServiceTests.cs`
  (+12 новых кейсов, NSubstitute, стиль существующих; 12 существующих кейсов не изменены):
  все applied (счётчики, сумма `updatedCommodities`, кэш по нормализованному имени); recorder
  ровно один раз с N draft'ами (suggested = исходное предложение, final = применённая категория,
  `AcceptedByAdmin`, `AcceptedByUserId`, токен `None`) и вызван после пополнения кэша (журнал -
  последняя БД-операция); смесь applied/conflict/not-found (причины, `null`-поля у skipped, нет
  кэша и draft'ов для skipped); все skipped - без ошибки и без вызова recorder; защитная ветка
  «репозиторий вернул меньше записей, чем id» → `skipped/not-found`, инвариант сохраняется
  (замечание ревьюера 2); пустой `ids` - нули, репозиторий/recorder/кэш не тронуты; исключение
  репозитория - пробрасывается, кэш и recorder не вызваны; сбой recorder - счётчики, `Results` и
  кэш не изменились, исключения нет; порядок `Results` = порядок `ids` при перемешанном ответе
  репозитория; decline - маппинг и инвариант, кэш/recorder не затронуты; decline пустой `ids` и
  сбой репозитория.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityWorkflowTests.cs`
  (+1 аргумент в приватном helper'е `CreateService` - `TimeProvider.System`, как у соседних
  helper'ов) - без этой строки тестовый проект не компилируется; тестовые кейсы не изменены.

Docs:

- `docs/plans/precategorized-bulk-operations.md` - строка статуса задачи 03.

Схема БД, миграции, индексы, конфигурация, nginx, Go-сервисы, фронтенд, HTTP-эндпоинты - не
тронуты (задача 04).

## Как проверено

- `cd Analytics && dotnet build` - 0 ошибок, 30 предупреждений (все предсуществующие NU1902/NU1903
  про уязвимые версии транзитивных пакетов, к задаче не относятся)
- `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedCommodityService"` -
  24/24 passed (12 существующих прогонов + 12 новых)
- `cd Analytics && dotnet test` - 823/823 passed до правок по замечаниям ревьюера
- **итоговый контрольный прогон `cd Analytics && dotnet test` - после внесения обоих принятых
  замечаний: `Passed! - Failed: 0, Passed: 824, Skipped: 0, Total: 824, Duration: 1 m 1 s`**
  (включая Testcontainers-сквозные `PrecategorizedCommodityWorkflowTests`/
  `PrecategorizedBulkRepositoryTests` и `Architecture/ProjectDependencyTests` - зависимости
  слоёв не нарушены)

Тесты полного набора требуют Docker (образ `postgres:16-alpine`).

## Итоги ревью

### Раунд 1 - APPROVED

Blockers нет: порядок «данные → кэш → журнал» виден по коду, `TryAdd` не вызывается при исключении
репозитория и для skipped, поштучные кейсы не изменены, тесты покрывают все 10 кейсов раздела
«Тесты» задачи.

Приняты и внедрены оба дешёвых неблокирующих замечания:

1. У комментария к draft'у уточнено, что fallback `OriginalProposedCategory ?? appliedCategory` -
   защита от нарушения контракта репозитория (у `Applied` поле заполнено по снимку `FOR UPDATE`),
   а не штатная семантика.
2. Добавлен unit-кейс `ConfirmManyAsync_missing_repository_entry_maps_to_not_found_and_keeps_invariant`:
   репозиторий возвращает меньше записей, чем id - отсутствующая запись становится
   `skipped/not-found`, инвариант `applied + skipped = requested` выполняется, draft и запись
   кэша - только по реальной `Applied`-строке.

Замечание 3 - заметка на задачу 04 (см. ниже), кода в этой задаче не требует.

После правок перепрогнаны сборка и полный набор тестов - 824/824 passed.

### Допущения и отклонения от текста плана

1. **Конструктор сервиса получил `TimeProvider`** (план: «замер длительности (`TimeProvider`)»).
   Это потребовало обновить два приватных test-helper'а `CreateService`
   (`PrecategorizedCommodityServiceTests`, `PrecategorizedCommodityWorkflowTests`) - добавлен
   аргумент `TimeProvider.System`. Сами тестовые кейсы не изменены; трактовка критерия «поштучные
   кейсы не изменены» - кейсы, не helper. DI не менялся (`TimeProvider.System` зарегистрирован).
2. **Recorder вызывается только при `drafts.Count > 0`.** Формулировка «один вызов после commit»
   относится к наличию применённых строк; при всех `skipped` вызов с пустым набором - no-op по
   контракту recorder, и его отсутствие строже соответствует «для skipped событий нет». При
   N > 0 applied вызов ровно один (зафиксировано тестом `Received(1)`).
3. **Защитная обработка нарушения контракта репозитория** (планом явно не описана): строка
   `Applied` с `null` `Name`/`ProposedCategory` не попадает в кэш/draft'ы (после commit ронять
   операцию нельзя); отсутствующая в ответе репозитория id трактуется как `skipped/not-found` -
   инвариант `applied + skipped = requested` сохраняется при любых входных данных (зафиксировано
   тестом из замечания ревьюера 2).
4. **Различение «дедлайн»/«клиент ушёл» оставлено эндпоинту** (§1.5 - задача 04): сервис логирует
   `Warning` при `OperationCanceledException` с отменённым токеном (прерывание), прочие
   исключения - `Error` с пробросом. «Число классифицированных строк» при прерывании сервису
   недоступно (оно в `Debug`-логе репозитория) - в `Warning` только число запрошенных id.
5. **Порог «долгой операции» - константа `BulkSlowOperationThreshold = 30 с`** в сервисе (план:
   «константа с комментарием»); новых настроек нет (F1).

### Заметки для задачи 04

- Эндпоинту при прерывании (`OperationCanceledException` при живом `RequestAborted` = дедлайн)
  стоит дополнить картину в своём `Warning`: число классифицированных строк в сервисном логе не
  содержится, оно только в `Debug`-логе репозитория (замечание ревьюера 3).
- Сервис не валидирует `ids` (пусто, > 200, `Guid.Empty`, дубликаты) и не знает про дедлайн - всё
  это слой эндпоинта; пустой список защищённо даёт нули, но `400` обязан вернуть эндпоинт.
- `acceptedByUserId = UserContext.UserId!.Value` - после `AdminAuthorization` пользователь
  гарантированно есть (как в поштучном `Confirm`).

## Заметки для коммита

Рекомендуемое сообщение коммита:

```text
t-03 Precategorized bulk service: cache and decision log after commit, bulk observability
```

На что обратить внимание при ручной проверке:

- Состав коммита: 1 файл Application (контракт), 1 файл Infrastructure (сервис), 2 файла тестов,
  строка статуса в плане и эта сводка. Ничего кроме этого в диффе быть не должно: миграций,
  индексов, настроек, правок `AGENTS.md`, `docker-compose.yml`, Go-сервисов, фронтенда и
  HTTP-эндпоинтов в задаче 03 нет.
- Быстрая проверка соответствия ADR прямо в диффе: `TryAdd` и `RecordDecisionsBestEffortAsync`
  находятся **после** `await _repository.ConfirmManyAsync(...)` и вне блоков `catch` (то есть
  достижимы только после commit); `RecordDecisionsBestEffortAsync` вызывается с
  `CancellationToken.None`; в `DeclineManyAsync` нет ни `_cache`, ни `_categoryDecisionRecorder`;
  поштучные `ConfirmAsync`/`DeclineAsync` в диффе не тронуты.
- Логи массовых операций не содержат наименований товаров и id (только счётчики и длительности) -
  требование «логи без ПД».
- Поведение поштучного пути считается неизменным, если зелёны существующие кейсы
  `PrecategorizedCommodityServiceTests` (они в этой задаче правке не подвергались, кроме
  одной строки helper'а с `TimeProvider.System`).
- Массовые эндпоинты ещё не существуют (задача 04) - ручная проверка через HTTP невозможна;
  приёмка сервиса - по юнит-тестам `PrecategorizedCommodityServiceTests` (24/24).
