# Задача 02 - Domain-контракт и batch-репозиторий: одна транзакция, `SELECT … FOR UPDATE`, set-based запись

- **Задача**: docs/plans/precategorized-bulk-operations.md#02-domain-контракт-и-batch-репозиторий-одна-транзакция-select-for-update-set-based-запись
- **Дата**: 2026-11-13
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Готов слой хранения массовых операций страницы «Предкатегоризация»: массовое подтверждение и
массовое отклонение строк очереди выполняются **одной транзакцией на весь список id** при явном
уровне изоляции `READ COMMITTED`. Строки читаются одним raw-запросом `SELECT … FOR UPDATE` в
детерминированном порядке `(normalized_name, merchant_id, id)`, и по этому же снимку до любых
изменений классифицируются: `proposed` - применить, `applied` - пропуск `already-applied`,
отсутствующий id - пропуск `not-found`. Пропуски не прерывают операцию, а технический сбой
откатывает весь список: `COMMIT` ровно один и он последний.

Категория применяется к товарам по точному имени через общий с поштучным путём шаг (предикат
«только без категории» больше не дублируется), очередь переводится в `applied` одним set-based
`UPDATE` с одним значением `applied_at` на батч, при отклонении - одним `DELETE`. Поштучные
методы репозитория семантически не изменены и из массового пути не вызываются.

Домен пополнился контрактом результата репозитория, инфраструктура - batch-методами; кэш, журнал
решений, HTTP-контракты и валидация `ids` в эту задачу не входили (задачи 03 и 04).

## Изменённые файлы

Domain:

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/PrecategorizedBulkRepositoryResults.cs`
  (новый) - `BulkConfirmRepositoryItem(Id, Outcome, Name, OriginalProposedCategory,
  ProposedCategory, UpdatedCommodities)` и `BulkDeclineRepositoryItem(Id, Outcome)`; значения -
  из снимка `SELECT … FOR UPDATE`, переиспользуется существующий `PrecategorizedOperationOutcome`
  (`Applied`/`Conflict`/`NotFound`). Зависимостей от Application/Infrastructure нет.
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/IPrecategorizedCommodityRepository.cs`
  (аддитивно) - `ConfirmManyAsync(ids, ct)` и `DeclineManyAsync(ids, ct)` с XML-doc: одна
  транзакция на весь список, `Conflict`/`NotFound` - результат строки, а не ошибка, любое
  исключение откатывает всё, кэш и журнал здесь не трогаются, поштучные методы из batch не
  вызываются. Существующие сигнатуры не изменены.

Infrastructure:

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs`
  - `ConfirmManyAsync`: пустой список - без транзакции; `BeginTransactionAsync(IsolationLevel.ReadCommitted)`;
    снимок под блокировкой одним raw-SQL (`Database.SqlQueryRaw<BulkSnapshotRow>` + `ToListAsync`,
    без LINQ-композиции, `ORDER BY normalized_name, merchant_id, id FOR UPDATE`); валидация
    категорий всего снимка до первого `UPDATE`; применение по именам в порядке снимка через общий
    helper; один `ExecuteUpdateAsync(status = applied, applied_at = @now)` по списку id
    (`@now` - одно чтение `TimeProvider`); ровно один `CommitAsync`; `Debug`-лог со счётчиками.
  - `DeclineManyAsync`: тот же снимок и классификация, `ExecuteDeleteAsync` только по
    классифицированным `proposed`, `commodities` не затрагиваются, `applied` остаётся.
  - `UpdateCommoditiesByNameAsync(exactName, category, ct)` - выделенный общий шаг обновления
    `commodities` (предикат `c.Name == exactName && (c.CategoryId == null || c.CategoryId == 0)`,
    сеттеры `CategoryId`/`CategoryName`, `IgnoreQueryFilters()` с комментарием о намеренном
    глобальном доступе - D8/NFR-5). Поштучный `ConfirmAsync` переведён на него **без изменения
    поведения**.
  - Вспомогательные: `LoadLockedSnapshotAsync`, `IsProposed`, `BuildConfirmResults`,
    `BuildDeclineResults` (порядок результата = порядок запроса, ADR-036 §1.1/§1.2).

Tests:

- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedBulkRepositoryTests.cs`
  (новый, 14 кейсов, Testcontainers PostgreSQL 16, стиль `PrecategorizedCommodityWorkflowTests`):
  атомарность confirm (триггер на `commodities` - откатывается и уже обновлённый первый товар) и
  decline (`BEFORE DELETE`); конфликт не откатывает остальных и не переписывает `applied_at`
  чужой строки; `not-found` для confirm и decline; границы страницы (15 `proposed`, батч 10 id -
  остальные 5 строки и их товары нетронуты); повторный запуск тем же батчем (все `Conflict`/
  `NotFound`, состояние данных не изменилось); обновление только товаров без категории у всех
  пользователей с корректным `updatedCommodities`; независимые наименования и обратный порядок id
  (3 прогона, без deadlock); конкурентная обработка (вторая транзакция держит блокировку, batch
  реально встаёт в ожидание по `pg_locks`, затем классифицирует строку как `Conflict`, остальные
  применяет); одно `applied_at` на батч; decline не трогает `commodities` и оставляет `applied`;
  пустой `ids` - пустой результат без исключения.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizationBackgroundServiceTests.cs`
  (+9 строк) - ручной `FakeRepository` реализует два новых метода контракта
  (`NotSupportedException`): без этого тесты не компилируются. Поведение тестов не изменено.

Docs:

- `docs/plans/precategorized-bulk-operations.md` - строка статуса задачи 02.

Схема БД, миграции, индексы, конфигурация, nginx, Go-сервисы, MongoDB, ADR и файл задачи -
не тронуты (ADR-036 §4, «Последствия»).

## Как проверено

- `cd Analytics && dotnet build` - 0 ошибок, 30 предупреждений (все предсуществующие NU1902/NU1903
  про уязвимые версии транзитивных пакетов, к задаче не относятся)
- `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedBulkRepositoryTests"` -
  14/14 passed
- `cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorized"` - 96/96 passed
- `cd Analytics && dotnet test` - 812/812 passed, 0 failed, 0 skipped (включая регрессию поштучных
  `PrecategorizedCommodityRepositoryTests` и `PrecategorizedCommodityWorkflowTests` **без их
  правок** и `Architecture/ProjectDependencyTests`)
- **итоговый контрольный прогон `cd Analytics && dotnet test` - уже после внесения всех трёх
  замечаний ревьюера: `Passed! - Failed: 0, Passed: 812, Skipped: 0, Total: 812, Duration: 1 m 2 s`**
  (цифры соответствуют текущему состоянию рабочего дерева)
- статические проверки соответствия ADR: `grep` по репозиторию - ровно один `CommitAsync` в каждом
  batch-методе, по одному `BeginTransactionAsync(IsolationLevel.ReadCommitted)`, set-based
  `ExecuteUpdateAsync`/`ExecuteDeleteAsync` по множеству id (цикл только по наименованиям
  `commodities`), предикат классификации в единственном месте (`IsProposed`)

Тесты требуют Docker (образ `postgres:16-alpine`).

## Итоги ревью

### Раунд 1 - APPROVED

Blockers нет: реализация соответствует ADR-036 §2 (одна транзакция `READ COMMITTED`, снимок под
`SELECT … FOR UPDATE` с детерминированным порядком блокировок, классификация до изменений,
set-based запись очереди, один commit), инвариантам плана (в том числе «поштучные контракты не
меняются» и «логи без ПД»), а тесты покрывают все 12 кейсов раздела «Тесты» задачи.

Приняты и внедрены **все три** неблокирующих замечания:

1. Предикат классификации сведён в приватный `IsProposed(BulkSnapshotRow)`: инлайн-сравнение
   `row.Status != (short)PrecategorizedCommodityStatus.Applied` использовалось в четырёх местах
   (confirm, decline и оба хелпера сборки результата), теперь оно в единственном месте - предикат
   не сможет разъехаться между путями.
2. `EnsureAssignableCategory` вынесен из цикла применения валидационным проходом **до** первого
   `UPDATE commodities` (fail-fast): повреждённое значение категории в очереди (`Undefined` или
   вне реестра) откатывает операцию, не успев выполнить ни одного statement'а по товарам.
   Наблюдаемое поведение прежнее - исключение до commit, полный откат.
3. Сообщение `EnsureAssignableCategory` переформулировано под оба сценария общего helper'а:
   `Category Undefined cannot be assigned to commodities or stored in the precategorization queue.`
   (ранее говорилось только про очередь). Текст в HTTP-ответы не попадает: эндпоинты
   предкатегоризации валидируют категорию до вызова сервиса; тестов на старый текст не было.

После правок перепрогнаны сборка и полный набор тестов - 812/812 passed.

### Допущения и отклонения от текста плана

1. **Алиасы колонок в raw-SQL не добавлены.** План предполагал явные алиасы («snake_case →
   PascalCase не маппится»). `ReceiptDbContext` и в проде, и в тестах сконфигурирован с
   `UseSnakeCaseNamingConvention()`, поэтому `proposed_category_id` корректно маппится на
   `ProposedCategoryId` - тот же механизм, что в существующем прецеденте `CandidateRow`
   (`merchant_id` → `MerchantId`). Проверено тестами: имена и категории из снимка читаются
   верно. Комментарий в коде это фиксирует.
2. **Сравнение `applied_at` в тестах - с допуском 1 мс**
   (`Assert.Equal(expected, actual, TimeSpan.FromMilliseconds(1))`, конвенция проекта:
   `UserAuthLinkRepositoryTests`, `PostgresReceiptRepositoryTests`). Postgres хранит `timestamptz`
   с точностью до микросекунды, .NET - до 100-нс тиков, поэтому побайтовое сравнение отброшенного
   значения падает на последней цифре. Смысл проверок сохранён: сравниваются значения, различающиеся
   минутами и сутками (время «второй» транзакции / посевное значение против времени батча).
3. **Валидация категории из снимка** (`EnsureAssignableCategory`) - случай, который ADR не описывает:
   `Undefined` или значение вне реестра в `proposed_category_id` трактуется как повреждённые данные,
   то есть технический сбой с полным откатом (решение 6), а не как частичное применение «мусорной»
   категории.
4. **`ids.Distinct()` перед SQL-параметром** - защитная дедупликация: дубликаты отсекает API
   (задача 04, `400`), но репозиторий не должен из-за них дважды блокировать или дважды применять
   строку. Результат при этом по-прежнему формируется по одной записью на каждый переданный id
   в порядке запроса.
5. **Правка `PrecategorizationBackgroundServiceTests.cs`** вынужденная: это единственный ручной
   фейк `IPrecategorizedCommodityRepository`, и после расширения контракта он обязан реализовать
   новые методы, иначе тестовый проект не компилируется. NSubstitute-моки правки не потребовали.
   Формально это отклонение от «не править существующие тесты», но оно не меняет ни одного
   поведения и ни одной проверки.
6. **Мелкие правки читаемости** (на поведение не влияют): `proposedRows`/`proposedUpdates` вместо
   двусмысленного `appliedRows`; `BuildDeclineResults` вынесен зеркально `BuildConfirmResults`;
   в XML-doc `BulkConfirmRepositoryItem` уточнён смысл `Name` (заполняется для строк снимка -
   `Applied` и `Conflict`, - и `null` для `NotFound`).

### Заметки для задач 03 и 04

- Задача 03 (сервис): `PrecategorizedOperationOutcome.Conflict` → `skipped`/`already-applied`,
  `NotFound` → `skipped`/`not-found`, `Applied` → `applied`/`declined` c категорией и
  `updatedCommodities` из снимка; `TryAdd` кэша и draft'ы журнала - **только** по `Applied`-строкам
  confirm и строго после возврата из репозитория (commit уже выполнен), один вызов recorder'а с
  `CancellationToken.None`. Поля `OriginalProposedCategory`/`ProposedCategory`/`UpdatedCommodities`
  заполнены только у `Applied`.
- Задача 04 (API): репозиторий **не** валидирует `ids` (пусто, > 200, `Guid.Empty`, дубликаты) и не
  знает про дедлайн - всё это слой эндпоинта. Инвариант `MaxBulkIds = GET MaxLimit = 200`
  появляется там же.
- Повторный запуск массовый операции безопасен по построению (тесты 8 и 9), поэтому задача 04 может
  не вводить идемпотентные ключи - достаточно `refresh()` на фронтенде.

## Заметки для коммита

Рекомендуемое сообщение коммита:

```text
t-02 Precategorized bulk repository: one transaction per batch, SELECT ... FOR UPDATE, set-based queue writes
```

На что обратить внимание при ручной проверке:

- **Файлы уже находятся в индексе git (`git add` выполнялся не исполнителем), причём в версии до
  правок по замечаниям ревьюера.** В рабочем дереве остались незакоммиченными
  `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs`
  (замечания 1-3) и `docs/plans/precategorized-bulk-operations.md` (статус `approved`). Перед
  коммитом нужно пере-стейджить эти файлы и эту сводку, иначе косметические правки раунда 1 в
  коммит не попадут; проверить, что `git diff` (unstaged) после стейджа пустой.
- Состав коммита: 2 файла Domain, 1 файл Infrastructure, 2 файла тестов, строка статуса в плане и
  эта сводка. Ничего кроме этого в диффе быть не должно: миграций, индексов, настроек, правок
  `AGENTS.md`, `docker-compose.yml`, Go-сервисов и фронтенда в задаче 02 нет.
- Быстрая проверка соответствия ADR прямо в диффе: в каждом batch-методе ровно один `CommitAsync`
  и он последний; `BeginTransactionAsync(IsolationLevel.ReadCommitted)` указан явно; `FOR UPDATE`
  идёт вместе с `ORDER BY normalized_name, merchant_id, id` в одном raw-SQL; поштучные
  `ConfirmAsync`/`DeleteProposedAsync` из batch-путей не вызываются.
- Поведение поштучного подтверждения считается неизменным, если зелёные
  `PrecategorizedCommodityRepositoryTests` и `PrecategorizedCommodityWorkflowTests` - они в этой
  задаче правке не подвергались.
- Тесты на конкурентность и атомарность поднимают реальный PostgreSQL (Testcontainers) и используют
  `pg_locks` для детерминированного ожидания блокировки (15 с на ожидание, 60 с на операцию). На
  очень медленном хосте эти два кейса - первые кандидаты на нестабильность; падать должны только
  они, а не логика.
