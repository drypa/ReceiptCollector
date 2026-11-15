# Задача 04 - API: `POST /batch/confirm`, `POST /batch/decline`, валидация, дедлайн, `500`

- **Задача**: docs/plans/precategorized-bulk-operations.md#04-api-post-batchconfirm-post-batchdecline-валидация-дедлайн-500-тесты-эндпоинтов
- **Дата**: 2026-11-13
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Готов HTTP-слой массовых операций страницы «Предкатегоризация» (ADR-036 §1, решения A3/F1):
два stateless-эндпоинта `POST /api/precategorized-commodities/batch/confirm` и
`POST /api/precategorized-commodities/batch/decline`. Клиент присылает только `ids`
(категория берётся из снимка строки в транзакции — D1), весь список обрабатывает уже
готовый сервисный batch-путь (задачи 02–03) одной транзакцией.

Порядок проверок выдержан по §1: `401` (нет пользователя) → `403` (не-админ, явным
`Results.StatusCode`) **до** разбора тела → валидация `400` по §1.3 (пусто / больше
`MaxBulkIds = 200` / `Guid.Empty` / дубликаты — тексты дословно) → сервис. `404`/`409`
на уровне батча не используются: уже подтверждённая и отсутствующая строки — `skipped`
с причиной при `200` (C1).

Подключён серверный дедлайн операции `BulkOperationTimeout = 90 с` (linked CTS поверх
`HttpContext.RequestAborted`) и различение исходов по §1.5: при уходе клиента отмена
пробрасывается и ответ не пишется; при срабатывании нашего дедлайна — `Warning` и `500`
с текстом «Bulk operation timed out and was rolled back; no rows were applied.»; при
прочим техническом сбое — `Error` с типом исключения и `SqlState` и `500` с текстом
«Bulk operation failed and was rolled back; no rows were applied.» Ответ `200` — агрегат
и `results` в порядке запроса, wire-имена lowercase, у `skipped`-строк поля категории и
счётчика — `null`. Поштучные `GET`, `PUT /{id}/category`, `POST /{id}/confirm`,
`DELETE /{id}` не изменены.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs`
  (+352 строк, 508 всего) — константы `MaxBulkIds = 200` (инвариант с `MaxLimit`),
  `BulkOperationTimeout = 90 с`, тексты `500`, имена действий логов; маршруты
  `/batch/confirm` и `/batch/decline` в существующем `MapPrecategorizedCommodityEndpoints()`
  (тот же тег `PrecategorizedCommodities`); хендлеры `ConfirmBatch`/`DeclineBatch`;
  приватные `ValidateBulkIds`, `RunBulkOperationAsync` (дедлайн + разделение «клиент
  ушёл»/«наш дедлайн»/технический сбой), `BulkOperationFailed` (текстовое тело, без
  ProblemDetails), `TryGetSqlState`, маппинги `ToBulkConfirmResponse`/`ToBulkDeclineResponse`;
  новые record'ы `BulkPrecategorizedRequest`, `BulkConfirmResponse`,
  `BulkConfirmItemResponse`, `BulkDeclineResponse`, `BulkDeclineItemResponse`.
  Существующие хендлеры и их контракты — без изменений.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedCommodityEndpointsTests.cs`
  (+494 строки, **только добавления** — удалённых строк в диффе нет) — 27 новых кейсов
  в стиле файла (NSubstitute, `UserContext.SetUserId`, исполнение `IResult` на
  `DefaultHttpContext`): 401 без пользователя (сервис не вызван); 403 не-админа до
  валидации тела, в том числе при `ids = null` (4 комбинации «эндпоинт × корректность
  тела»); `400` для отсутствующего тела / `ids = null` / пустого списка, 201 id,
  `Guid.Empty` с индексом, дубликата — для confirm и decline; `200` с агрегатом и
  `results` в порядке запроса, инварианты `applied + skipped = requested` и
  `declined + skipped = requested`, `null`-поля у `skipped`; `200` ровно для 200 id и
  `400` для 201; инвариант `MaxBulkIds = MaxLimit` рефлексией + поведенчески (страница
  `limit = 200` проходит `GET` и валидацию `ids`); wire-контракт JSON (lowercase-имена,
  `outcome`/`reason` значения, отсутствие `updatedCommodities` у decline); `500` с
  текстом при дедлайне и при исключении сервиса (проверка кода, `content-type` и тела
  исполнением на `HttpContext`); отсутствие ответа при отменённом `RequestAborted`;
  передача `UserContext.UserId` в `ConfirmManyAsync`; регистрация обоих POST-маршрутов
  и тега.
- `docs/plans/precategorized-bulk-operations.md` — строка статуса задачи 04.

Схема БД, миграции, индексы, конфигурация, nginx, Go-сервисы, фронтенд — не тронуты
(задача 04 — только Api-слой и его тесты).

## Как проверено

- `cd Analytics && dotnet build` — `Build succeeded. 0 Error(s)`, предупреждений `CS` — 0
  (в выводе только предсуществующие `NU1902`/`NU1903` про уязвимые транзитивные пакеты)
- `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedCommodityEndpoints"` —
  `Passed: 41, Failed: 0` (14 существующих кейсов + 27 новых)
- **итоговый контрольный прогон `cd Analytics && dotnet test` (после правок по замечаниям
  ревьюера): `Passed! - Failed: 0, Passed: 851, Skipped: 0, Total: 851, Duration: 1 m 2 s`**
  (до задачи было 824; включая Testcontainers-сквозные `PrecategorizedBulkRepositoryTests`/
  `PrecategorizedCommodityWorkflowTests` и `Architecture/ProjectDependencyTests` —
  зависимости слоёв не нарушены)
- `git status --short` — изменены ровно три файла выше; `git add`/`git commit`/`git push`
  не выполнялись

Полный набор тестов требует Docker (образ `postgres:16-alpine`).

## Итоги ревью

### Раунд 1 - APPROVED

Blockers нет: порядок проверок, тексты `400`/`500`, дедлайн и различение «наш дедлайн»/
«клиент ушёл» соответствуют ADR-036 §1.3/§1.5; существующие endpoint-кейсы не изменены;
инвариант `MaxBulkIds` = `GET MaxLimit` выдержан.

Приняты и внедрены два незаблокирующих замечания:

1. **Инвариант `MaxBulkIds == MaxLimit` усилен тестом** — добавлен
   `MaxBulkIds_equals_get_max_limit`: значения обеих приватных констант сравниваются
   рефлексией (`ReadEndpointIntConstant`), расхождение ловится сразу, а не только на
   границе 200/201. Поведенческий тест (200 id проходят, 201 — `400`, `GET limit = 200`
   проходит) оставлен: он проверяет применимость инварианта, а не равенство чисел.
2. **Косметика:** строковые литералы `"confirm"`/`"decline"` вынесены в константы
   `BulkConfirmAction`/`BulkDeclineAction`; значения сохранены — они совпадают с именами
   действий в логах сервиса, чтобы строки слоёв коррелировались.

Оставлено без изменений:

3. **`Warning` при дедлайне не содержит «числа классифицированных строк»** (замечание 2).
   Значение формируется в репозитории и попадает только в его `Debug`-лог (ADR-036 §7
   требует `Debug` для ids и наименований), сервису и эндпоинту оно недоступно — иначе
   пришлось бы поднимать счётчики через слой сервиса или логировать id выше `Debug`.
   Эндпоинт дополняет картину: в `Warning` пишутся `requested`, `TimeoutSeconds` и явная
   отсылка, что число классифицированных строк — в `Debug`-логе репозитория. Требование
   задачи 03 («Warning должен дополнять картину») выполнено в этой форме; при желании
   архитектор может уточнить формулировку §7 отдельно.

После правок перепрогнаны сборка и полный набор тестов — 851/851 passed.

### Допущения и отклонения от текста плана

1. **`Information`-лог на эндпоинте не дублируется.** §7 ADR-036 уже закрыт сервисом
   (задача 03: счётчики, разбивка пропусков по причинам, длительность, признак отмены).
   На уровне эндпоинта добавлены только `Warning` (дедлайн) и `Error` (тип исключения +
   `SqlState`) — иначе одна операция давала бы две почти одинаковые `Information`-строки.
2. **Замечание из сводки задачи 03 учтено:** в `Warning` при дедлайне пишутся `requested`,
   `TimeoutSeconds` и явная отсылка, что число классифицированных строк доступно только в
   `Debug`-логе репозитория.
3. **`SqlState` без зависимости от Npgsql:** Api-слой читает `DbException.SqlState`
   (переопределён у `PostgresException`) по цепочке `InnerException`. `Error` содержит
   `SqlState` при `40P01`/constraint/`CommandTimeout`, жёсткая связь слоя с провайдером
   не появилась.
4. **`500` — `Results.Text(..., "text/plain; charset=utf-8", statusCode: 500)`**: текстовое
   тело в стиле модуля (ADR-027 п. 7), без ProblemDetails; `503`/`504` не используются.
5. **Тело запроса — `[FromBody] BulkPrecategorizedRequest?`** (nullable): JSON-literal
   `null` и `{}` дают `400 "ids must not be empty."`, а не NRE/500 (прецедент
   `MerchantEndpoints`). Полностью отсутствующее тело отклоняет сам minimal-API-байндер
   (`400` до хендлера) — то же поведение, что у существующего
   `CommodityBatchCategorizationEndpoints.Suggest`.
6. **Дедлайн создаётся после авторизации и валидации** (не «на входе» буквально): 90 с —
   бюджет именно транзакции, а вывод значения в F1 («минус ~30 с на авторизацию, ответ,
   сериализацию и сеть») предполагает, что авторизация в бюджет не входит. `CancellationTokenSource`
   живёт до конца операции, `using` освобождает таймер.
7. **`MaxBulkIds` — отдельный литерал `200`** (как в плане/ADR: «константа эндпоинта по
   образцу `MaxLimit`»), а не `= MaxLimit`, чтобы изменение `GET`-пагинации не меняло
   молча контракт массовых операций; инвариант зафиксирован комментариями на обеих
   константах, тестом на равенство значений и поведенческим тестом.
8. **Тесты `500` проверяют фактическое исполнение `IResult`** (код, `content-type`, текст
   тела): текст ошибки — часть контракта §1.5, а не деталь реализации.

## Заметки для коммита

Рекомендуемое сообщение коммита:

```text
t-04 Precategorized bulk API: batch confirm/decline endpoints, validation, 90s deadline, 500 handling
```

На что обратить внимание при ручной проверке:

- Состав коммита: 1 файл Api (эндпоинты), 1 файл тестов, строка статуса в плане и эта
  сводка. Миграций, индексов, настроек, правок `AGENTS.md`, `docker-compose.yml`, nginx,
  Go-сервисов и фронтенда в задаче 04 нет.
- В диффе плана строка `03 → committed` — не правка этой задачи (её выставил оркестратор
  после коммита задачи 03); правка задачи 04 — только строка статуса `04`.
- Быстрая проверка соответствия ADR прямо в диффе: `AdminAuthorization.AuthorizeAsync`
  вызывается **до** `ValidateBulkIds` (не-админ с некорректным телом получает `403`);
  `catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)`
  стоит раньше «дедлайнового» `catch` и делает `throw` без записи ответа; `404`/`409`
  в batch-хендлерах отсутствуют вовсе; `MaxBulkIds` и `MaxLimit` равны 200.
- Регрессия поштучного пути: существующие кейсы `PrecategorizedCommodityEndpointsTests`
  (`GET`/`PUT`/`confirm`/`DELETE`) в этой задаче не правились — в диффе тестового файла
  нет ни одной удалённой строки.
- Ручная HTTP-проверка (для задачи 10): `POST /api/precategorized-commodities/batch/confirm`
  с `{ "ids": [ … ] }` под админом даёт `200` со сводкой; под не-админом — `403` при любом
  теле; `ids: []` — `400 "ids must not be empty."`; 201 id — `400 "ids must contain at
  most 200 items."`. Фронтенд подключается в задачах 05–08.
