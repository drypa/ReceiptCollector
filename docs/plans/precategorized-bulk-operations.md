# Декомпозиция: массовые подтверждение и отклонение на странице «Предкатегоризация»

> Источники: задача [precategorized-bulk-operations](../tasks/precategorized-bulk-operations.md)
> (решения заказчика 1–7 в редакции 2026-11-13, критерии приёмки, edge cases, «Не входит
> в объём») и [ADR-036](../adr/036-precategorized-bulk-operations.md) — основной документ
> реализации (ключевые решения **A3** — два stateless-эндпоинта, вся страница одним запросом;
> **B2** — одна транзакция на батч, `READ COMMITTED`, `SELECT … FOR UPDATE`; **C1/C3** —
> `NotFound`/`Conflict` → `skipped` с причиной при HTTP `200`; **D1** — только `ids`, категория
> из снимка строки; **E1** — накопление draft'ов и один вызов recorder'а после commit;
> **F1** — `MaxBulkIds = 200`, дедлайн 90 с, без новых настроек; §4 «Слои и карта изменений»;
> §5 UX-контракт; §7 наблюдаемость; §8 план тестирования; §9 «Явно зафиксированные семантики»).
> Дополняющие ADR: **027** (очередь, поштучная транзакция confirm, стиль админских эндпоинтов,
> `400/404/409`), **028** (пакетный stateless-эндпоинт, правила `400`, блокировка контролов,
> нормализация опустевшей страницы §6; **порционность не переиспользуется** — отклонена
> заказчиком), **030** (фон — без изменений), **033** (журнал решений: одно событие на строку,
> после commit, recorder не бросает), **034** (очищенный ключ, partial unique `proposed`),
> **019** (кэш, `TryAdd`), **020** (тосты), **011** (frontend-тестовой инфраструктуры нет).
>
> **Этот документ — только декомпозиция.** Новых архитектурных решений он не содержит и не
> заменяет ADR-036. Спорное место эскалируется к @software-architect, исполнителем
> самостоятельно не решается. Реализация — строго по одной задаче за цикл (skill
> `task-review-gate`): реализация → AI-ревью → ручное ревью → коммит человеком.
>
> **Отклонённые формы решения не воскрешаются:** порционность (клиентский чанкинг),
> транзакция на строку, счётчик/прогресс в UX, серверное понятие «страницы»,
> `202 + jobId`, SSE-стриминг. Возврат к ним — только через новое решение заказчика.
>
> Изменения — только Analytics (.NET API + фронтенд). Go-сервисы (`backend/`, `bot/`), MongoDB,
> nginx, схема БД, миграции, индексы и конфигурация **не меняются** (ADR-036 §4, «Последствия»).
>
> **Тесты входят в задачи по слоям** (конвенция проекта: `docs/plans/categorization-decision-log.md`,
> `docs/plans/commodities-page-categorization.md`) — отдельной «тестовой» задачи нет. Финальная
> задача 10 только прогоняет наборы и выполняет ручную приёмку, новых тестов не добавляет.

## Ход выполнения

| NN | Задача | Зависимости | Статус |
| -- | ------ | ----------- | ------ |
| 01 | Application: модели массовых результатов и wire-константы исходов/причин | — | committed |
| 02 | Domain-контракт и batch-репозиторий: одна транзакция, `SELECT … FOR UPDATE`, set-based запись | 01 | committed |
| 03 | Сервис: контракт `ConfirmManyAsync`/`DeclineManyAsync`, кэш и журнал после commit, наблюдаемость | 02 | committed |
| 04 | API: `POST /batch/confirm`, `POST /batch/decline`, валидация, дедлайн, `500`, тесты эндпоинтов | 03 | committed |
| 05 | Frontend: типы результатов и API-клиент массовых операций | 04 | committed |
| 06 | Frontend: хук массовых операций (снимок страницы, один запрос, busy/сводка/ошибка) | 05 | committed |
| 07 | Frontend: компонент кнопок, диалог подтверждения, busy-строка и сводка | 06 | approved |
| 08 | Frontend: интеграция в страницу, блокировки контролов, аддитивный `disabled` таблицы | 07 | pending |
| 09 | Frontend: нормализация опустевшей страницы в `usePrecategorizedCommodities` | 08 | pending |
| 10 | Финальная проверка контрактов, регрессии и ручная приёмка | 01…09 | pending |

Строки таблицы задают единственный допустимый порядок исполнения: задача N стартует только после
того, как задача N-1 проверена человеком и **закоммичена**. Параллельного выполнения нет.
Значения `in-progress` / `review` / `approved` / `committed` выставляют исполнитель и
`project-manager`; при старте все задачи — `pending`.

## Обозначения

| Поле | Значение |
|---|---|
| Приоритет | **P0** — без этого нет сквозного сценария или нарушается критерий приёмки; **P1** — требуется edge case / качество; **P2** — не блокирует |
| Оценка | человеко-дни одного разработчика, без учёта ревью и ручной приёмки |
| Зависит от | задачи, которые должны быть **влиты** (committed) до старта |
| Проверка | конкретные команды и/или ручные сценарии |

Общие команды:

```bash
cd Analytics && dotnet build
cd Analytics && dotnet test
cd Analytics/frontend && npm run lint && npm run build
```

## Сводка и порядок исполнения

| NN | Задача | Слой | Приор. | Оценка | Зависит от |
|---|---|---|---|---|---|
| 01 | Application-модели массовых результатов + wire-константы | Application | P0 | 0.25 | — |
| 02 | Batch-репозиторий: `READ COMMITTED`, `FOR UPDATE`, классификация, set-based запись | Domain/Infra | P0 | 1.5 | 01 |
| 03 | Сервис: оркестрация, `TryAdd` после commit, один вызов recorder'а, логи §7 | App/Infra | P0 | 1.0 | 02 |
| 04 | API: два эндпоинта, `MaxBulkIds = 200`, дедлайн 90 с, `500` | Api | P0 | 0.75 | 03 |
| 05 | FE: типы + `confirmPrecategorizedBatch`/`declinePrecategorizedBatch` | FE | P0 | 0.25 | 04 |
| 06 | FE: хук `usePrecategorizedBulkOperations` | FE | P0 | 0.5 | 05 |
| 07 | FE: `PrecategorizedBulkActions` (кнопки, диалог, busy, сводка) | FE | P0 | 0.75 | 06 |
| 08 | FE: страница, блокировки, `disabled` в таблице | FE | P0 | 0.75 | 07 |
| 09 | FE: нормализация опустевшей страницы | FE | P1 | 0.25 | 08 |
| 10 | Финальная проверка, регрессия, ручная приёмка | все | P0 | 0.5–1.0 | 01…09 |

**Критический путь:** 01 → 02 → 03 → 04 → 05 → 06 → 07 → 08 → 09 → 10. Суммарно
последовательно ≈ 6.5–7 чел.-дн. до ручной приёмки. Параллельных блоков нет: контракт данных
(01) определяет репозиторий (02), репозиторий — сервис (03), сервис — эндпоинты (04), без
эндпоинтов фронтенд не проверяем.

## Общие инварианты для всех задач

1. **Единица атомарности массовой операции — весь запрос** (ADR-036 B2): одна транзакция,
   `READ COMMITTED`, ровно один `COMMIT` и он последний. Любое исключение/таймаут/отмена до
   commit не оставляет применённых строк.
2. **Логический конфликт — результат строки, а не ошибка** (C1): `applied`-строка →
   `skipped/already-applied`, отсутствующая строка → `skipped/not-found`, HTTP `200`.
   `404`/`409` на уровне батча не используются.
3. **Применяемая категория — из снимка строки** (D1). Клиент присылает **только** `ids`;
   снимок `[{ id, categoryId }]` и массовое присвоение категории — вне объёма.
4. **Порядок в ответе — порядок запроса** (ADR-036 §1.1): `Results` сопоставляются по id и
   отдаются в порядке `ids`, а обрабатываются в порядке `(normalized_name, merchant_id, id)`.
5. **Инвариант ответа:** `applied + skipped = requested` (confirm), `declined + skipped =
   requested` (decline); `requested` = число id в запросе.
6. **Кэш (ADR-019)** пополняется `TryAdd` только для `applied`-строк confirm и только **после**
   commit; `TryAdd` first-wins/no-op сохраняется. Decline кэш не трогает.
7. **Журнал (ADR-033, E1):** одно событие на `applied`-строку, **один** вызов
   `ICategorizationDecisionRecorder.RecordAsync(drafts, CancellationToken.None)` **после** commit,
   строго последняя БД-операция запроса; сбой журнала не влияет на счётчики, коды и кэш.
8. **Поштучные контракты не меняются** (ADR-036 §6): `GET`, `PUT /{id}/category`,
   `POST /{id}/confirm`, `DELETE /{id}` — те же пути, тела, коды, транзакция на строку.
   Поштучный `ConfirmAsync`/`DeleteProposedAsync` **не вызываются** из batch-пути.
9. **Никаких новых настроек, миграций, индексов, изменений nginx/`docker-compose.yml`/`.env*`/
   `AGENTS.md`** (F1: константы в коде). `MaxBulkIds` = `GET MaxLimit` — инвариант: менять вместе.
10. **Frontend без логики оркестрации:** один `POST`, снимок `items`, busy-state без чисел,
    финальная сводка, `refresh()`. Ни порций, ни счётчиков прогресса, ни повторных запросов.
11. **Логи без ПД** (ADR-027/028/033): наименования товаров — только `Debug`, ids — `Debug`.
12. Каждая задача оставляет `cd Analytics && dotnet test` зелёным **без правок существующих
    тестов**, кроме случаев, явно перечисленных в задаче (общий helper `commodities` в 02).
13. Зависимости проектов (`Architecture/ProjectDependencyTests`): Domain не знает
    Application/Infrastructure/Api; Application — только Domain; Infrastructure —
    Application + Domain; Api — все.

---

## 01. Application: модели массовых результатов и wire-константы исходов/причин

**Приоритет:** P0 · **Оценка:** 0.25 чел.-дн. · **Зависит от:** —

### Цель
Зафиксировать контракт данных массовых операций (ADR-036 §1.1/§1.2, §4 «Application») до
реализации, чтобы репозиторий, сервис, эндпоинты и тесты опирались на один набор типов и
wire-имён.

### Что входит
- Новый файл `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/PrecategorizedBulkModels.cs`:
  - `BulkItemOutcomes` (статический класс wire-имён, по образцу
    `PrecategorizedCommodityStatusNames` / `CategorizationDecisionSources`):
    `Applied = "applied"`, `Declined = "declined"`, `Skipped = "skipped"`, `All`
    (точное сравнение `Ordinal`);
  - `BulkSkipReasons`: `AlreadyApplied = "already-applied"`, `NotFound = "not-found"`, `All`;
  - `BulkConfirmItemResult(Guid Id, string Outcome, string? Reason, int? CategoryId,
    string? CategoryName, int? UpdatedCommodities)`;
  - `BulkConfirmResult(int Requested, int Applied, int Skipped, int UpdatedCommodities,
    IReadOnlyCollection<BulkConfirmItemResult> Results)`;
  - `BulkDeclineItemResult(Guid Id, string Outcome, string? Reason)`;
  - `BulkDeclineResult(int Requested, int Declined, int Skipped,
    IReadOnlyCollection<BulkDeclineItemResult> Results)`.
- XML-doc каждого типа: ссылка на ADR-036 §1.1/§1.2, инвариант
  `applied + skipped = requested`, смысл `updatedCommodities` = 0 (решение состоялось, D10 ADR-027),
  отсутствие `updatedCommodities` у decline (асимметрия намеренная, §1.2).
- `CategoryId`/`CategoryName`/`UpdatedCommodities`/`Reason` — nullable: у `skipped`-строк
  значения не заполняются (`null`; сериализуются как `null`, клиент ориентируется на `outcome` —
  стиль проекта, прецедент `PrecategorizedCommodityDto.MerchantName`).

### Что НЕ входит
- Методы `IPrecategorizedCommodityRepository` и `IPrecategorizedCommodityService` (02, 03).
- Domain-модели уровня репозитория (02) — их форма описывает снимок строки под блокировкой.
- HTTP-DTO, JSON-атрибуты, валидация (04).

### Тесты
Новый `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedBulkModelsTests.cs`
(unit, без инфраструктуры):
- wire-имена дословно: `applied`, `declined`, `skipped`, `already-applied`, `not-found`;
- `BulkItemOutcomes.All` / `BulkSkipReasons.All` содержат ровно эти значения (контракт с
  фронтендом и endpoint-тестами; ловит случайное переименование);
- модель результата допускает `skipped`-строку без категории и без `updatedCommodities`.

### Критерии готовности
- `dotnet build` и `dotnet test` зелёные; ни одна существующая сигнатура не изменена.
- Типы лежат в Application и не зависят от Infrastructure/Api (Architecture-тесты зелёные).

### Проверка
```bash
cd Analytics && dotnet build && dotnet test
```

---

## 02. Domain-контракт и batch-репозиторий: одна транзакция, `SELECT … FOR UPDATE`, set-based запись

**Приоритет:** P0 · **Оценка:** 1.5 чел.-дн. · **Зависит от:** 01

### Цель
Реализовать хранение массовой операции по ADR-036 §2: один снимок строк под блокировкой,
классификация `proposed`/`already-applied`/`not-found` **до** изменений, применение в
детерминированном порядке, одна транзакция `READ COMMITTED` и один commit.

### Что входит
- **Domain** — `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/IPrecategorizedCommodityRepository.cs`
  (аддитивно, существующие методы не меняются):
  - `Task<IReadOnlyCollection<BulkConfirmRepositoryItem>> ConfirmManyAsync(
     IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)`;
  - `Task<IReadOnlyCollection<BulkDeclineRepositoryItem>> DeclineManyAsync(
     IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)`;
  - XML-doc: одна транзакция на весь список; `Applied`/`Conflict` (= already-applied)/
    `NotFound` — результат строки, не ошибка; любое исключение → полный откат; кэш и журнал
    здесь не трогаются.
- **Domain** — новый `Modules/Commodities/PrecategorizedBulkRepositoryResults.cs`:
  - `BulkConfirmRepositoryItem(Guid Id, PrecategorizedOperationOutcome Outcome, string? Name,
    CommodityCategory? OriginalProposedCategory, CommodityCategory? ProposedCategory,
    int UpdatedCommodities)` — значения из **снимка** `SELECT … FOR UPDATE`;
  - `BulkDeclineRepositoryItem(Guid Id, PrecategorizedOperationOutcome Outcome)`.
  Переиспользуется существующий `PrecategorizedOperationOutcome` (`Applied`/`Conflict`/`NotFound`).
- **Infrastructure** — `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs`:
  1. `ids.Count == 0` → пустая коллекция **без** открытия транзакции (защитно).
  2. `BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)` — уровень изоляции указывается
     явно (ADR-036 F1/§2.2: в `REPEATABLE READ`/`SERIALIZABLE` конкурентное обновление дало бы
     `40001` = технический сбой вместо пропуска).
  3. Снимок + блокировка — **один raw-SQL** через `Database.SqlQueryRaw<Row>` +
     `ToListAsync` (без LINQ-композиции, чтобы `FOR UPDATE` не оказался во вложенном запросе):
     ```sql
     SELECT id, name, proposed_category_id, original_proposed_category_id, status
     FROM precategorized_commodities
     WHERE id = ANY(@ids)
     ORDER BY normalized_name, merchant_id, id
     FOR UPDATE
     ```
     Алиасы колонок под имена свойств row-типа (snake_case → PascalCase не маппится
     автоматически; прецедент — `CandidateRow` с `Name`/`MerchantId`).
     Порядок `(normalized_name, merchant_id, id)` — единственный допустимый (детерминированные
     блокировки, ADR-036 §2.3).
  4. Классификация по снимку: `status = applied` → `Conflict`; id отсутствует в результате →
     `NotFound`; иначе `Applied`.
  5. **Confirm**, для каждой `Applied`-строки в том же детерминированном порядке — обновление
     `commodities` по **точному** имени через приватный helper
     `UpdateCommoditiesByNameAsync(string exactName, CommodityCategory category,
     CancellationToken ct)`, **общий с поштучным `ConfirmAsync`** (предикат
     `c.Name == exactName && (c.CategoryId == null || c.CategoryId == 0)`, сеттеры
     `CategoryId`/`CategoryName`, `IgnoreQueryFilters()` с комментарием о намеренном глобальном
     доступе — D8/NFR-5). Поштучный `ConfirmAsync` переводится на этот helper **без изменения
     поведения**. Set-based `UPDATE … FROM unnest` не применять: порядок блокировок должен быть
     нашим, а не порядком сканирования.
  6. Один set-based перевод очереди: `UPDATE precategorized_commodities SET status = 1,
     applied_at = @now WHERE id = ANY(@appliedIds)` (через `ExecuteUpdateAsync`, без 200
     tracked-сущностей); `@now` — **одно** значение `TimeProvider.GetUtcNow()` на батч.
  7. **Decline:** `ExecuteDeleteAsync` только по id классифицированных `Applied`-строк;
     `commodities` не трогается, `applied`-строки не удаляются.
  8. `CommitAsync` — ровно один, последний. При исключении транзакция откатывается
     (dispose/rollback), исключение пробрасывается без обёртки.
  9. Логи: `Debug` — количество id и количество классифицированных по причинам (без
     наименований на уровне выше `Debug`).
- Существующие `ConfirmAsync`/`DeleteProposedAsync`/`UpdateProposedCategoryAsync` — семантика и
  коды без изменений; batch-путь их **не вызывает**.

### Что НЕ входит
- Кэш, журнал решений, маппинг в Application-модели (03).
- Валидация `ids`, дедлайн, HTTP-коды (04) — репозиторий получает уже валидированный список.
- Индексы на `commodities(name)`, изменение схемы, миграции (риск 6 ADR-027 — отдельное решение).
- Автоматический retry при deadlock (F4 — отклонён).

### Тесты
Новый `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedBulkRepositoryTests.cs`
(Testcontainers PostgreSQL, стиль `PrecategorizedCommodityWorkflowTests` / `PrecategorizedCommodityRepositoryTests`):

1. **Атомарность confirm:** временный триггер на `commodities`, бросающий исключение для
   заданного имени → `ConfirmManyAsync` бросает; **ни одна** строка очереди не стала `applied`,
   `commodities` не изменены (проверка после вызова).
2. **Атомарность decline:** триггер на `precategorized_commodities` (`BEFORE DELETE`) →
   исключение; ни одна строка не удалена.
3. **Конфликт не откатывает:** 1 `applied` + 2 `proposed` → 2 применены, 1 в `Conflict`;
   у существующей `applied`-строки `applied_at` не изменился.
4. **NotFound:** несуществующий id → `NotFound`, остальные строки применены (confirm) /
   удалены (decline).
5. **Границы страницы:** 15 `proposed`, батч из 10 id → изменены ровно эти 10, остальные 5
   остались `proposed`, их товары не тронуты.
6. **Повторный запуск:** тот же батч второй раз → все строки `Conflict`/`NotFound`, состояние
   данных не изменилось (и для confirm, и для decline).
7. **Только товары без категории:** товар с уже присвоенной категорией не перезаписан; товары
   разных пользователей с тем же именем обновлены; `updatedCommodities` = число обновлённых.
8. **Независимые наименования:** две строки с разными именами → два независимых обновления;
   запрос с обратным порядком id не даёт deadlock/таймаута (3 прогона) — детерминированный
   порядок блокировок.
9. **Конкурентная обработка:** вторая транзакция подтверждает одну из строк и коммитит →
   batch ждёт блокировку и классифицирует её как `Conflict`, остальные применяет.
10. **Одно `applied_at`:** у всех применённых строк батча одинаковое значение.
11. **Decline не трогает товары:** ни одного изменения `commodities`; удалены только
    `proposed`-строки; `applied` остаётся.
12. **Пустой `ids`:** пустой результат, исключения нет.

### Критерии готовности
- `dotnet test` зелёный; `PrecategorizedCommodityRepositoryTests` и
  `PrecategorizedCommodityWorkflowTests` проходят **без правок** (регрессия поштучного confirm
  после выноса helper'а).
- В коде ровно один `COMMIT` на batch-операцию; поштучные методы не вызываются из batch.
- `ExecuteUpdate/ExecuteDelete` используются на множестве id, а не в цикле по строкам
  (цикл допустим только по **наименованиям** `commodities` — §2.1 ADR).

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorized"
cd Analytics && dotnet test
```

---

## 03. Сервис: контракт `ConfirmManyAsync`/`DeclineManyAsync`, кэш и журнал после commit, наблюдаемость

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** 02

### Цель
Собрать бизнес-результат массовой операции: маппинг исходов репозитория в контракт 01,
пополнение кэша и запись журнала строго после commit, сводные логи ADR-036 §7.

### Что входит
- **Application** — `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/IPrecategorizedCommodityService.cs`
  (аддитивно, существующие сигнатуры не меняются):
  - `Task<BulkConfirmResult> ConfirmManyAsync(IReadOnlyCollection<Guid> ids,
    Guid acceptedByUserId, CancellationToken cancellationToken = default)`;
  - `Task<BulkDeclineResult> DeclineManyAsync(IReadOnlyCollection<Guid> ids,
    CancellationToken cancellationToken = default)`;
  - XML-doc: одна транзакция на список; `NotFound`/`Conflict` → `skipped` с причиной;
    технический сбой → исключение (эндпоинт отдаёт `500`); кэш и журнал — после commit;
    recorder получает `CancellationToken.None`.
- **Infrastructure** — `PrecategorizedCommodityService.cs`:
  - `ConfirmManyAsync`:
    1. `ids.Count == 0` → нулевой результат (`requested = 0`) **без** обращения к репозиторию;
    2. замер длительности (`TimeProvider`), вызов `_repository.ConfirmManyAsync(ids, ct)`;
    3. для каждой `Applied`-строки — `_cache.TryAdd(CommodityNameNormalizer.NormalizeName(Name),
       ProposedCategory)` (строго после commit; first-wins/no-op — ADR-019);
    4. draft'ы **только** по `Applied`-строкам: `CommodityName = Name`,
       `SuggestionSource = CategorizationDecisionSources.Precategorized`,
       `SuggestedCategory = OriginalProposedCategory`, `FinalCategory = ProposedCategory`,
       `AcceptedByUserId = acceptedByUserId`, `AcceptedByAdmin: true`; затем **один** вызов
       существующего best-effort метода записи журнала с `CancellationToken.None`
       (последняя БД-операция запроса);
    5. сборка `BulkConfirmResult` в порядке `ids` (сопоставление по id): `Applied` →
       `outcome = applied` + `categoryId`/`categoryName`/`updatedCommodities` из снимка,
       `Conflict` → `skipped`/`already-applied`, `NotFound` → `skipped`/`not-found`
       (поля категории/счётчика — `null`); `requested = ids.Count`;
       `updatedCommodities` = сумма по `Applied`; инвариант `applied + skipped = requested`;
  - `DeclineManyAsync`: вызов репозитория; `Applied` → `declined`, `Conflict` →
    `skipped`/`already-applied`, `NotFound` → `skipped`/`not-found`; кэш и журнал **не
    затрагиваются**;
  - §7 ADR-036 логи:
    - `Information` — одна строка на операцию: действие (`bulk-confirm`/`bulk-decline`),
      `requested`, `applied`/`declined`, `skipped` с разбивкой по причинам,
      `updatedCommodities`, длительность в мс, признак отмены;
    - `Warning` — все строки пропущены (чужое окно / устаревший снимок); длительность выше
      порога 30 с (константа с комментарием);
    - `Error` — сбой журнала (существующий catch уже логирует `Error` с числом событий);
    - наименования — только `Debug`.
- DI не меняется (регистрация сервиса существует).

### Что НЕ входит
- Транзакция, блокировки, SQL (02).
- HTTP-валидация, дедлайн, коды ответов (04).
- Изменение поштучного `ConfirmAsync`/`DeclineAsync` (поведение и логика журнала — как есть).
- Отдельный путь подтверждения без журнала/кэша, фоновые ретраи.

### Тесты
`Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedCommodityServiceTests.cs`
(NSubstitute; существующие кейсы не меняются, добавляются новые):

1. Все строки `Applied` → `applied = N`, `skipped = 0`, `updatedCommodities` = сумма;
   `TryAdd` вызван по каждой применённой строке с нормализованным именем и применённой
   категорией.
2. Recorder вызван **ровно один раз** с N draft'ами: `suggested` = исходное предложение,
   `final` = применённая категория, `AcceptedByAdmin = true`, `AcceptedByUserId` передан;
   токен — `CancellationToken.None`.
3. Смесь `Applied` + `Conflict` + `NotFound` → корректные счётчики и причины; для `skipped`
   нет draft'ов и нет `TryAdd`.
4. Все строки `skipped` → `applied = 0`, `skipped = N`, результата-исключения нет.
5. Пустой `ids` → нули, репозиторий не вызван, `TryAdd`/recorder не вызваны.
6. Исключение репозитория → пробрасывается; ни `TryAdd`, ни recorder не вызваны.
7. Recorder бросает → счётчики, `Results` и кэш не меняются, исключения нет.
8. Порядок `Results` = порядок `ids` (перемешанный вход, включая `skipped` в середине).
9. Decline: успех и пропуски корректны; `TryAdd` и recorder не вызваны вовсе.
10. Две `Applied`-строки с разными наименованиями → два отдельных draft'а (группировки по
    имени нет).

### Критерии готовности
- `dotnet test` зелёный; поштучные кейсы `PrecategorizedCommodityServiceTests` не изменены.
- Порядок «данные → кэш → журнал» виден по коду; `TryAdd` не вызывается при исключении
  репозитория и для `skipped`.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedCommodityService"
cd Analytics && dotnet test
```

---

## 04. API: `POST /batch/confirm`, `POST /batch/decline`, валидация, дедлайн, `500`

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** 03

### Цель
Дать фронтенду два stateless-эндпоинта (A3) с валидацией по §1.3, серверным дедлайном F1 и
раз различением «наш дедлайн» / «клиент ушёл» по §1.5.

### Что входит
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs`:
  - константы:
    - `MaxBulkIds = 200` с комментарием «инвариант с `MaxLimit = 200` у `GET` (любая страница,
      которую `GET` способен вернуть, обрабатывается одним запросом); менять только вместе»;
    - `BulkOperationTimeout = TimeSpan.FromSeconds(90)` с комментарием «ниже
      `proxy_read_timeout 120 s`; запас ~30 с на авторизацию/ответ/сеть»;
  - маршруты в существующем `MapPrecategorizedCommodityEndpoints()`:
    `group.MapPost("/batch/confirm", ConfirmBatch)` и
    `group.MapPost("/batch/decline", DeclineBatch)` (литеральный сегмент не конфликтует с
    `{id:guid}`; порядок маппинга значения не имеет);
  - тело: `BulkPrecategorizedRequest(IReadOnlyList<Guid>? Ids)`;
  - ответы (локальные record'ы модуля):
    `BulkConfirmResponse(int Requested, int Applied, int Skipped, int UpdatedCommodities,
    IReadOnlyCollection<BulkConfirmItemResponse> Results)`,
    `BulkConfirmItemResponse(Guid Id, string Outcome, string? Reason, int? CategoryId,
    string? CategoryName, int? UpdatedCommodities)`,
    `BulkDeclineResponse(int Requested, int Declined, int Skipped,
    IReadOnlyCollection<BulkDeclineItemResponse> Results)`,
    `BulkDeclineItemResponse(Guid Id, string Outcome, string? Reason)`
    (маппинг из Application-моделей 01; nullable-поля `skipped`-строк — `null`, стиль проекта);
  - порядок проверок: `AdminAuthorization.AuthorizeAsync` (`401`/`403` **до** разбора тела) →
    валидация `400` → сервис. Тексты — дословно §1.3:
    `"ids must not be empty."` (нет/`null`/пусто),
    `"ids must contain at most 200 items."`,
    `"ids[i] is not a valid GUID."` (`Guid.Empty`),
    `"ids contains duplicate id '…'."`;
  - дедлайн: `using var cts = CancellationTokenSource.CreateLinkedTokenSource(
    httpContext.RequestAborted); cts.CancelAfter(BulkOperationTimeout);` → сервис с `cts.Token`;
  - обработка результатов:
    - `OperationCanceledException`, когда `httpContext.RequestAborted.IsCancellationRequested`
      → исключение **пробрасывается**, ответ не пишется (клиента уже нет);
    - `OperationCanceledException` при живом запросе (сработал дедлайн) → `Warning` + `500` с
      текстом `"Bulk operation timed out and was rolled back; no rows were applied."`;
    - прочие исключения (БД/сеть/constraint/deadlock `40P01`/`CommandTimeout`) → `Error` с типом
      исключения и `SqlState` (если есть) + `500` с текстом
      `"Bulk operation failed and was rolled back; no rows were applied."`;
    - текстовые тела (без ProblemDetails), `503`/`504` не используются;
  - `acceptedByUserId = UserContext.UserId!.Value` для confirm (после `AdminAuthorization`
    пользователь гарантированно есть — как в поштучном `Confirm`);
  - XML-doc модуля: ссылка на ADR-036 §1, `404`/`409` на уровне батча не используются (C1).
- `Program.cs` — изменений нет (`MapPrecategorizedCommodityEndpoints()` уже вызывается).

### Что НЕ входит
- Изменение `GET`, `PUT /{id}/category`, `POST /{id}/confirm`, `DELETE /{id}`.
- Валидация категорий, снимков категорий, любого тела кроме `ids`.
- Идемпотентные ключи, retry, `202 + jobId`, стриминг ответа.

### Тесты
`Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedCommodityEndpointsTests.cs`
(стиль проекта: `UserContext.SetUserId`, NSubstitute, исполнение `IResult` на `DefaultHttpContext`;
существующие кейсы — без правок):

1. `401` без пользователя (сервис не вызван) — для обоих эндпоинтов.
2. `403` для не-админа, в том числе при `ids = null` → именно `403`, не `400`
   (проверка порядка «права до валидации»).
3. `400`: `ids` отсутствует/`null`/пустой; 201 id; `Guid.Empty`; дубликат — для confirm и
   для decline.
4. `200`: агрегат (`requested`, `applied`, `skipped`, `updatedCommodities`) и `results` в
   порядке запроса; `requested` = числу id; `applied + skipped = requested`.
5. `200` для **ровно 200** id (инвариант «страница максимального размера проходит валидацию»,
   §1.4) и `400` для 201.
6. `200` decline: `declined`/`skipped`, поле `updatedCommodities` отсутствует в контракте.
7. `500` с текстом про таймаут: сервис бросает `OperationCanceledException`, `RequestAborted`
   не отменён (дедлайн); проверяется код и тело.
8. `500` с текстом про откат: сервис бросает любое другое исключение.
9. Клиент ушёл: `RequestAborted` отменён, сервис бросает `OperationCanceledException` →
   хендлер пробрасывает исключение (`IResult` не возвращается, тело не пишется).
10. `UserContext.UserId` передаётся в `ConfirmManyAsync` (Received с ожидаемым значением).
11. Регистрация маршрутов/тега: `/api/precategorized-commodities/batch/confirm` и
    `/batch/decline` присутствуют в `EndpointDataSource`, тег `PrecategorizedCommodities`
    сохранён (если в файле уже есть тест регистрации — расширить его).

### Критерии готовности
- `dotnet test` зелёный; существующие endpoint-тесты `GET/PUT/confirm/DELETE` не изменены.
- Не-админ не получает `400`/`200` ни при каком теле (только `403`).
- `MaxBulkIds` и `MaxLimit` равны 200 и связаны комментарием; поведенческий тест 5 зелёный.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedCommodityEndpoints"
cd Analytics && dotnet test
```

---

## 05. Frontend: типы результатов и API-клиент массовых операций

**Приоритет:** P0 · **Оценка:** 0.25 чел.-дн. · **Зависит от:** 04

### Цель
Дать фронтенду типизированный доступ к двум новым операциям с одним запросом на страницу и
локализованными сообщениями об ошибках (ADR-036 §1.5, §4 «Frontend»).

### Что входит
- `Analytics/frontend/src/types/precategorizedCommodity.ts` (аддитивно):
  - `PrecategorizedBulkAction = 'confirm' | 'decline'`;
  - `PrecategorizedBulkItemOutcome = 'applied' | 'declined' | 'skipped'`;
  - `PrecategorizedBulkSkipReason = 'already-applied' | 'not-found'`;
  - `PrecategorizedBulkConfirmItem { id: string; outcome: PrecategorizedBulkItemOutcome;
    reason?: PrecategorizedBulkSkipReason | null; categoryId?: number | null;
    categoryName?: string | null; updatedCommodities?: number | null }`;
  - `PrecategorizedBulkConfirmResult { requested: number; applied: number; skipped: number;
    updatedCommodities: number; results: PrecategorizedBulkConfirmItem[] }`;
  - `PrecategorizedBulkDeclineItem { id: string; outcome: PrecategorizedBulkItemOutcome;
    reason?: PrecategorizedBulkSkipReason | null }`;
  - `PrecategorizedBulkDeclineResult { requested: number; declined: number; skipped: number;
    results: PrecategorizedBulkDeclineItem[] }`.
- `Analytics/frontend/src/api/precategorizedCommodities.ts` (аддитивно):
  - `confirmPrecategorizedBatch(ids: string[]): Promise<PrecategorizedBulkConfirmResult>` —
    `POST /api/precategorized-commodities/batch/confirm`, `Content-Type: application/json`,
    `body: JSON.stringify({ ids })`, `credentials: 'include'`;
  - `declinePrecategorizedBatch(ids: string[]): Promise<PrecategorizedBulkDeclineResult>` —
    аналогично на `/batch/decline`;
  - ошибки: `throw new Error(...)`; для `5xx` и сетевого сбоя — **локализованный** текст
    «Операция не выполнена, изменения не применены.» (серверный текст в тост не подставляется,
    §1.5); `401/403` — уже существующие локализованные ветки `readError`; поведение `readError`
    для **поштучных** вызовов не меняется (5xx-ветка добавляется локально в batch-функциях либо
    отдельным параметром).
- Никаких изменений существующих `fetchPrecategorizedCommodities`,
  `confirmPrecategorized`, `declinePrecategorized`, `updatePrecategorizedCategory`.

### Что НЕ входит
- Хук, компоненты, блокировки, тосты (06–08).
- Клиентская оркестрация: порции, повторные запросы, счётчики прогресса (запрещено решением 4–5).
- Валидация `ids` на клиенте сверх «пустой список не отправляем».

### Тесты
Автотестов фронтенда в проекте нет (ADR-011 E1). Барьер:
`npm run lint && npm run build`; типы проверяются компилятором (`tsc` в составе build).

### Критерии готовности
- `npm run lint` и `npm run build` зелёные; новых зависимостей нет.
- Обе функции делают ровно один `fetch` без тела-обёртки сверх `{ ids }`.
- Ни одна существующая функция API не изменена.

### Проверка
```bash
cd Analytics/frontend && npm run lint && npm run build
```

---

## 06. Frontend: хук массовых операций (снимок страницы, один запрос, busy/сводка/ошибка)

**Приоритет:** P0 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** 05

### Цель
Собрать состояние массовой операции по §5 ADR-036: снимок `items` в момент подтверждения
диалога, один запрос, `isRunning`, итоговая сводка с причинами пропусков, ошибка, `refresh()`
в `finally`. Без порций, без счётчика, без тостов (презентация — 07).

### Что входит
- Новый `Analytics/frontend/src/hooks/usePrecategorizedBulkOperations.ts`:
  ```ts
  interface BulkOperationSummary {
    action: 'confirm' | 'decline';
    applied?: number;        // confirm
    declined?: number;       // decline
    skipped: number;
    updatedCommodities?: number;                 // confirm
    reasons: Partial<Record<'already-applied' | 'not-found', number>>;
  }
  usePrecategorizedBulkOperations({ onCompleted }: { onCompleted: () => void }): {
    isRunning: boolean;
    summary: BulkOperationSummary | null;
    error: string | null;
    run(action, items: PrecategorizedCommodityItem[]): Promise<void>;
    reset(): void;
  }
  ```
  - `run`: повторный вызов при `isRunning` — no-op (вторая операция невозможна, edge case
    «двойное нажатие»); `ids = items.map(i => i.id)` — **снимок на момент подтверждения**;
    `isRunning = true`, `summary = null`, `error = null`; ровно один вызов
    `confirmPrecategorizedBatch`/`declinePrecategorizedBatch`; из ответа собирается `summary`
    (причины агрегируются по счётчикам из `results`: `already-applied`, `not-found`);
    `catch` → `error = message` (для 5xx/сети — «изменения не применены»); `finally` →
    `isRunning = false`, `onCompleted()` (автообновление списка и после успеха, и после ошибки);
  - `reset()` — очистка `summary`/`error` (вызывается при смене страницы/поиска из 08).
- Хук **не** показывает тосты, **не** управляет диалогом, **не** перезапрашивает список во
  время операции.

### Что НЕ входит
- Кнопки, диалог, busy-строка, сводка как разметка (07).
- Блокировки контролов страницы (08).
- Сохранение/кэш снимка между рендерами и любые повторные попытки запроса.

### Тесты
Автотестов фронтенда нет. Барьер: `npm run lint && npm run build` + проверка типов.

### Критерии готовности
- `npm run lint`, `npm run build` зелёные.
- В хуке ровно один сетевой вызов на `run`, нет циклов/чанков/счётчиков.
- `onCompleted` вызывается в `finally` при любом исходе.

### Проверка
```bash
cd Analytics/frontend && npm run lint && npm run build
```

---

## 07. Frontend: компонент кнопок, диалог подтверждения, busy-строка и сводка

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** 06

### Цель
Реализовать UX-контракт ADR-036 §5: две кнопки на всю страницу, диалог с количеством строк,
busy-state без прогресса, читаемая сводка и локализованная ошибка.

### Что входит
- Новый `Analytics/frontend/src/components/PrecategorizedBulkActions.tsx`:
  - пропсы (минимально необходимые; точные имена — на усмотрение исполнителя):
    `items: PrecategorizedCommodityItem[]`, `isRunning: boolean`, `summary`, `error`,
    `disabled: boolean`, `onRun(action, ids: string[])`;
  - две кнопки «Подтвердить все» / «Отклонить все»; `disabled = disabled || isRunning ||
    items.length === 0`; `title` с причиной недоступности (пустая страница / загрузка /
    операция выполняется);
  - `CustomDialog` (существующий, без изменений) — один экземпляр, состояние
    `{ action: 'confirm' | 'decline' }`; тексты **дословно**:
    - confirm: «Подтвердить все N предложений на этой странице? Категории будут применены ко
      всем товарам с этими названиями у всех пользователей.»;
    - decline: «Отклонить все N предложений на этой странице? Предложения будут удалены
      безвозвратно. Категории товаров не изменятся.»;
    - `N = items.length` (строки страницы, не `X-Total-Count`);
    - «Отмена»/закрытие — никаких запросов;
    - при подтверждении — снимок `ids = items.map(i => i.id)`, `onRun(action, ids)`, диалог
      закрывается сразу (busy-состояние живёт в хуке);
  - busy-строка: `role="status"`, `aria-live="polite"`, спиннер + «Подтверждаем предложения…» /
    «Отклоняем предложения…»; **никаких чисел, процентов, «X из N»**;
  - сводка (после завершения, не исчезает сама): «Подтверждено: N, пропущено: M»
    (+ «обновлено товаров: K») / «Отклонено: N, пропущено: M»; при наличии пропусков — причины
    агрегированно: «уже обработаны в другом окне» (`already-applied`), «больше не существуют»
    (`not-found`);
  - ошибка: inline «Операция не выполнена, изменения не применены.» + `toast.error` по конвенции
    ADR-020; успех — `toast.success` со сводкой; тост показывается один раз на завершённую
    операцию (без повторов на ре-рендерах);
  - стили: переиспользовать существующие классы (`controls`, `state`, `spinner`,
    `category-display`); при нехватке — компактный аддитивный блок в
    `Analytics/frontend/src/App.css` с комментарием (ссылка на ADR-036 §5).
- Компонент не знает о запросах: всё состояние выполнения приходит из хука (06).

### Что НЕ входит
- Монтирование в страницу и блокировки остальных контролов (08).
- Изменение `CustomDialog` (busy-режим ему не добавляется — YAGNI, §5).
- Выборочное выделение строк, чекбоксы, выбор целевой категории (вне объёма задачи).
- Изменение поштучных действий таблицы.

### Тесты
Автотестов фронтенда нет. Барьер: `npm run lint && npm run build`; визуальная проверка
разметки — в задаче 08/10.

### Критерии готовности
- `npm run lint`, `npm run build` зелёные.
- Тексты диалога и сводки совпадают с §5; `N` берётся из `items.length`.
- В компоненте нет числового прогресса и нет повторных запросов при повторном клике.

### Проверка
```bash
cd Analytics/frontend && npm run lint && npm run build
```

---

## 08. Frontend: интеграция в страницу, блокировки контролов, аддитивный `disabled` таблицы

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** 07

### Цель
Собрать сквозной сценарий на странице `/precategorized`: кнопки в шапке, полная блокировка
контролов на время операции, автообновление списка, сохранение сводки.

### Что входит
- `Analytics/frontend/src/components/PrecategorizedCommoditiesPage.tsx`:
  - `const bulk = usePrecategorizedBulkOperations({ onCompleted: refresh })`;
  - `<PrecategorizedBulkActions … />` в шапке рядом с «Обновить» (не в таблице);
  - `controlsLocked = bulk.isRunning` — блокируются: селект размера страницы, «Обновить», поле
    поиска, `Pagination` (пропы `disabled` + `disabledTitle` уже существуют — проверено),
    таблица; `title` с подсказкой о причине блокировки;
  - кнопки массовых операций недоступны при `data.length === 0`, `isLoading`, `error`,
    `bulk.isRunning` (пустая страница — критерий приёмки);
  - `bulk.reset()` при смене `currentPage`/`search` (сводка сохраняется до следующего действия
    или смены страницы);
  - `refresh()` уже вызывается хуком в `finally` — дублирующих вызовов не добавлять.
- `Analytics/frontend/src/components/PrecategorizedCommodityTable.tsx`:
  - аддитивный проп `disabled?: boolean` (по умолчанию `false` — поведение прежних вызовов
    не меняется);
  - `disabled={isSaving || disabled}` на селекте категории и на поштучных кнопках
    «Подтвердить»/«Отклонить»; `title` «Дождитесь завершения массовой операции»;
  - `savingId`-логика и диалог поштучного отклонения — без изменений.
- Никаких изменений `Pagination` (пропы уже есть) и `CustomDialog`.

### Что НЕ входит
- Нормализация опустевшей страницы (09).
- Изменение порядка колонок, стилей таблицы, поштучных контрактов.
- Блокировка навигации в сайдбаре/маршрутов.

### Тесты
Автотестов фронтенда нет. Барьер: `npm run lint && npm run build` + ручная проверка:
- 2+ страницы: «Подтвердить все» → диалог с `items.length` → busy без чисел → сводка →
  список обновился, строки ушли из очереди;
- во время операции недоступны пагинация, поиск, размер страницы, «Обновить» и поштучные
  кнопки строк;
- «Отмена» в диалоге — ни одного сетевого запроса (DevTools);
- двойной клик по кнопке — второй запрос не уходит;
- пустая страница — кнопки неактивны.

### Критерии готовности
- `npm run lint`, `npm run build` зелёные.
- При активном поиске в запрос уходят только id отрисованных строк (проверка payload в DevTools).
- Смена страницы/поиска во время операции невозможна.

### Проверка
```bash
cd Analytics/frontend && npm run lint && npm run build
# ручной сценарий на dev-стенде (страница /precategorized)
```

---

## 09. Frontend: нормализация опустевшей страницы в `usePrecategorizedCommodities`

**Приоритет:** P1 · **Оценка:** 0.25 чел.-дн. · **Зависит от:** 08

### Цель
Закрыть edge case «последняя страница опустела после операции» (ADR-036 §5, §3 «Факт по коду»):
страница после массовой операции не должна показывать пустую таблицу при наличии строк на
предыдущих страницах.

### Что входит
- `Analytics/frontend/src/hooks/usePrecategorizedCommodities.ts` — по образцу
  `useCommodities` (ADR-028 §6):
  - `loadPage` → именованное функциональное выражение `load(page, allowNormalization = true)`;
  - после ответа: если `pageItems.length === 0 && effectiveTotalItems > 0 &&
    normalizedPage > computedTotalPages && allowNormalization` → `load(computedTotalPages, false)`
    (не более одной повторной попытки — защита от цикла);
  - `effectiveTotalItems === 0` → пустое состояние штатно, без догрузки;
  - добавить защиту «ответ устаревшего/отменённого запроса игнорируется»
    (`abortRef.current !== controller`), как в `useCommodities`: без неё цепочка из двух
    запросов может затереть свежие данные;
  - публичный контракт хука (`data`, `currentPage`, `totalPages`, `totalItems`, `refresh`,
    `goToPage`, …) не меняется; debounce поиска и сброс на первую страницу — без изменений.

### Что НЕ входит
- Изменение `fetchPrecategorizedCommodities` (клиент API), `totalItems`-fallback и пагинации.
- Нормализация других хуков (`useMerchants`, `useReceipts`).
- Изменение серверного `GET` (сервер о «странице» не знает — ADR-028 §1.3).

### Тесты
Автотестов фронтенда нет. Барьер: `npm run lint && npm run build` + ручная проверка:
- последняя страница (например, 2 из 2) опустела после массовой операции → автоматический
  переход на предыдущую доступную, данные отрисованы, ошибок нет;
- единственная страница опустела → пустое состояние «Нет предложений.», ошибок нет,
  кнопки массовых операций неактивны;
- обычная смена страницы/поиска/размера — поведение прежнее (регрессия).

### Критерии готовности
- `npm run lint`, `npm run build` зелёные.
- Повторный запрос выполняется не более одного раза на загрузку (проверяется в DevTools/Network).

### Проверка
```bash
cd Analytics/frontend && npm run lint && npm run build
```

---

## 10. Финальная проверка контрактов, регрессии и ручная приёмка

**Приоритет:** P0 · **Оценка:** 0.5–1.0 чел.-дн. · **Зависит от:** 01…09

### Цель
Подтвердить, что сквозной сценарий выполняется, атомарность и семантика пропусков работают на
реальной БД, а существующие контракты не изменились (ADR-036 §6, §9).

### Что входит
- Прогон всех наборов: `cd Analytics && dotnet test`, `cd Analytics && dotnet build`,
  `cd Analytics/frontend && npm run lint && npm run build`.
- Ручной прогон на dev-стенде (чек-лист ADR-036 §8 + критерии приёмки задачи):
  1. страница с несколькими страницами пагинации: «Подтвердить все» → диалог с
     `items.length` → busy без счётчика → сводка → автообновление; строки ушли из очереди;
  2. «Отклонить все»: предупреждение о безвозвратном удалении, сводка «Отклонено: N,
     пропущено: M», `commodities` не изменились;
  3. активный поиск: затрагиваются только строки страницы (`X-Total-Count` > `items.length`);
     строки других страниц не тронуты;
  4. строка подтверждена во втором окне → попадает в «пропущено» с причиной «уже обработаны в
     другом окне», операция не падает;
  5. «Отмена»/закрытие диалога → ничего не меняется;
  6. пустая страница → массовые кнопки неактивны;
  7. последняя страница опустела → переход на предыдущую; единственная страница → пустое
     состояние, без ошибок;
  8. страница на 100 строк: busy-state без чисел, UI не «висит»; длительность операции
     замерена и зафиксирована в сводке (ADR-036 §2.4);
  9. обрыв сети / дедлайн: сообщение «изменения не применены», список консистентен после
     обновления, повторный запуск безопасен;
  10. двойной клик по кнопке → второй запрос не уходит; параллельная массовая операция из
      второго окна → вторая ждёт/пропускает обработанное;
  11. не-админ: кнопок нет; прямой `curl` на оба эндпоинта → `403`; поштучные эндпоинты
      (`GET`, `PUT /{id}/category`, `POST /{id}/confirm`, `DELETE /{id}`) отвечают как раньше;
  12. после массового подтверждения: категория есть в in-memory кэше (`suggest` по этому
      названию даёт `source = cache`) и в журнале `categorization_decisions` — по одному
      событию на `applied`-строку, `suggested` = исходное предложение, `final` = применённая
      категория; массовое отклонение событий не даёт;
  13. отредактированная администратором категория (через `PUT /{id}/category` до запуска)
      применяется товарам массовым подтверждением.
- Проверка отсутствия изменений вне объёма: `git status --short` — нет правок в `backend/`,
  `bot/`, `docker-compose*.yml`, `.env*`, `AGENTS.md`, миграциях; новых настроек нет.
- Сверка с ADR-036 «Явно зафиксированные семантики» (§9) и §6 «Влияние на существующие
  контракты».
- Сводка изменений и результатов прогона — в `docs/reviews/precategorized-bulk-operations/`
  (конвенция проекта).

### Что НЕ входит
- Новые функции (выборочное выделение, массовая смена категории, операции на других страницах).
- Индекс на `commodities(name)` и замеры планов — отдельное решение (риск 6 ADR-027).
- Правки архитектуры на ходу: найденное несоответствие ADR — эскалация к @software-architect.

### Тесты
Только прогон существующих наборов; новых тестов задача не добавляет. Найденные дефекты
исправляются в рамках соответствующей задачи (01–09) отдельным циклом.

### Критерии готовности
- `dotnet test` (Analytics, все проекты, включая `Architecture/ProjectDependencyTests`) и
  `npm run lint`/`npm run build` зелёные.
- Все пункты «Критерии приёмки» задачи отмечены и подтверждены ручной проверкой.
- Сводка с результатами, замерами длительности и остаточными рисками — в
  `docs/reviews/precategorized-bulk-operations/`.

### Проверка
```bash
cd Analytics && dotnet test
cd Analytics/frontend && npm run lint && npm run build
git status --short
```

---

## Трассировка требований → задачи

| Требование (задача / ADR-036) | Задачи |
|---|---|
| Два эндпоинта, один запрос на страницу (решение 4, A3) | 01 (модели), 02 (batch-репозиторий), 04 (маршруты и валидация), 06–08 (один `fetch`) |
| Одна транзакция на батч, полный откат при техническом сбое (решение 6, B2) | 02 (транзакция, тесты атомарности), 04 (`500` с текстом), 10 (ручной сбой сети) |
| Логические конфликты → `skipped` при `200` (C1/C3) | 02 (классификация под `FOR UPDATE`), 03 (маппинг причин), 04 (нет `404/409`), 07 (человекочитаемые причины) |
| Область = строки текущей страницы, поиск учитывается в её пределах (решение 2) | 06 (снимок `items`), 08 (payload = отрисованные строки) |
| Диалог подтверждения с количеством, отказ ничего не меняет (решение 3) | 07 (тексты, `N = items.length`), 08 (проверка «нет запроса») |
| Busy без прогресса, затем сводка и автообновление (решение 5) | 06 (`isRunning`, `summary`, `onCompleted`), 07 (busy/сводка), 08 (блокировки) |
| Категория — из строки на момент запуска, включая правки админа (D1) | 02 (снимок `proposed_category_id`), 03 (draft `final`), 10 (п. 13 чек-листа) |
| Идентично поштучному: кэш и журнал для applied (ADR-019/033) | 03 (`TryAdd`, один recorder), 10 (п. 12 чек-листа) |
| Права: не-админ не видит кнопки и получает `403` | 04 (порядок `401 → 403 → валидация`), 08 (гейт страницы уже есть), 10 (curl) |
| Пустая страница: операции недоступны | 07/08 (`disabled` при `items.length === 0`) |
| Опустевшая последняя страница остаётся работоспособной (edge case) | 09 (нормализация хука), 10 (п. 7 чек-листа) |
| Повторный запуск безопасен | 02 (повторный батч → все `skipped`), 10 (п. 9 чек-листа) |
| Автотесты покрывают новые API-операции (решение 7) | 02 (Testcontainers), 03 (unit), 04 (endpoints) |
| `MaxBulkIds = GET MaxLimit` (инвариант F1) | 04 (константа + комментарий + поведенческий тест на 200/201) |
| Контракты существуют без изменений (ADR-036 §6) | 02 (поштучные методы не рефакторятся по транзакции), 04 (коды и пути), 10 (регрессия и `git status`) |
| Никаких новых настроек/миграций/индексов (§4) | все задачи; проверка в 10 |

## Риски и точки эскалации

| Риск / ловушка | Где | Действие |
|---|---|---|
| `FOR UPDATE` через `SqlQueryRaw` требует row-типа с алиасами под PascalCase | 02 | Алиасы в SQL (прецедент `CandidateRow`); это деталь реализации, не архитектурное решение |
| `ExecuteUpdateAsync` + `IgnoreQueryFilters()` на `commodities` | 02 | Проверить, что глобальный фильтр не срезает выборку; тест «товары разных пользователей обновлены» |
| Случайный вызов поштучных `ConfirmAsync`/`DeleteProposedAsync` в цикле | 02, 03 | Запрещено: вложенные транзакции ломают B2; общий только приватный helper по имени |
| Deadlock с многоимённым путём страницы «Товары» (ADR-036 §2.3) | 02, 10 | Детерминированный порядок в bulk + полный откат + безопасный повтор; выравнивание порядка в «Товарах» — **отдельная задача** (эскалация, не править здесь) |
| Длительность > 90 с на больших объёмах `commodities` | 02, 10 | Замер на dev обязателен (п. 8 чек-листа); при выходе за бюджет — эскалация (уменьшение размера страницы / индекс на `name` — отдельное решение ADR-027, риск 6) |
| `CommandTimeout` Npgsql (30 с) срабатывает раньше дедлайна | 02, 04 | Это технический сбой → полный откат и `500`; ничего не настраивать (F2 отклонён) |
| Отмена клиента путается с серверным дедлайном | 04 | Различение по `HttpContext.RequestAborted`; обязательные тесты 7–9 |
| Сбой журнала теряет до N событий батча | 03 | Best-effort (NFR-3 ADR-033): `Warning` со счётчиком, коды и счётчики не меняются |
| Соблазн добавить порции/прогресс «для скорости» | 05–08 | Прямо запрещено решениями 4–5; ревью должно отклонять |
| Соблазн «улучшить» `CustomDialog` busy-режимом | 07 | YAGNI (§5): busy живёт в хуке |
| Новые настройки/таймауты в конфигурации | все | F2 отклонён: только константы в коде; иное — эскалация к @software-architect |
| Отсутствие автотестов фронтенда | 05–09 | `npm run lint`/`build` + ручные сценарии (ADR-011 E1); вся бизнес-логика — на сервере |

## Замечания для реализации (красные флаги на ревью)

1. **Не менять** поштучные эндпоинты и их семантику: `GET`, `PUT /{id}/category`,
   `POST /{id}/confirm`, `DELETE /{id}` — пути, тела, коды, транзакция на строку остаются
   прежними (ADR-036 §6).
2. **Не вызывать** существующие поштучные методы репозитория из batch-пути (вложенные
   транзакции, частичная фиксация).
3. **Не вводить** `404`/`409` на уровне массового запроса: это `skipped` при `200` (C1).
4. **Не реконструировать** «страницу» на сервере (`limit/offset/search` в теле) — вариант A6
   отклонён.
5. **Не добавлять** клиентскую порционность, прогресс, счетчики, повторные запросы (A1/B3
   отклонены).
6. **Журнал** — строго один вызов, `CancellationToken.None`, после commit, последняя
   БД-операция запроса; `TryAdd` — только для `applied` и только после commit.
7. **Порядок блокировок** — только `(normalized_name, merchant_id, id)`; порядок ответа —
   порядок `ids`.
8. **`MaxBulkIds` = 200** и `MaxLimit` = 200: менять только вместе, комментарий обязателен.
9. **Никаких** миграций, индексов, опций конфигурации, правок nginx/`docker-compose`/`.env`/
   `AGENTS.md` (ADR-036 §4).
10. При любом противоречии с ADR-036 или критериями приёмки — эскалация к @software-architect
    с указанием пункта ADR, а не самостоятельное решение.
