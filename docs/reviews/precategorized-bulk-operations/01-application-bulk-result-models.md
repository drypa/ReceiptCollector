# Задача 01 - Application: модели массовых результатов и wire-константы исходов/причин

- **Задача**: docs/plans/precategorized-bulk-operations.md#01-application-модели-массовых-результатов-и-wire-константы-исходовпричин
- **Дата**: 2026-11-13
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Зафиксирован контракт данных массовых операций страницы «Предкатегоризация» до реализации
остальных слоёв. В Application появился один новый файл с wire-константами исходов и причин
пропуска (`applied`/`declined`/`skipped`, `already-applied`/`not-found`) и четырьмя моделями
результата: агрегаты подтверждения и отклонения плюс результат по каждой строке. Изменений
существующих контрактов нет: только аддитивные типы, ни одна сигнатура не тронута.

Типы — единственная точка, на которую будут опираться задача 02 (batch-репозиторий), задача 03
(сервис и маппинг исходов репозитория) и задача 04 (HTTP-ответы): последние маппят Application-модели
в свои response-записи и не переопределяют wire-имена заново.

## Изменённые файлы

Новый файл Application
(`Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/`):

- `PrecategorizedBulkModels.cs`:
  - `BulkItemOutcomes` - константы `Applied = "applied"`, `Declined = "declined"`,
    `Skipped = "skipped"` и неизменяемый `All` (`FrozenSet` с `StringComparer.Ordinal`):
    сравнение точное, регистр и дефис значимы;
  - `BulkSkipReasons` - `AlreadyApplied = "already-applied"`, `NotFound = "not-found"`, `All`
    с теми же свойствами;
  - `BulkConfirmItemResult(Guid Id, string Outcome, string? Reason, int? CategoryId,
    string? CategoryName, int? UpdatedCommodities)`;
  - `BulkConfirmResult(int Requested, int Applied, int Skipped, int UpdatedCommodities,
    IReadOnlyCollection<BulkConfirmItemResult> Results)`;
  - `BulkDeclineItemResult(Guid Id, string Outcome, string? Reason)`;
  - `BulkDeclineResult(int Requested, int Declined, int Skipped,
    IReadOnlyCollection<BulkDeclineItemResult> Results)`.
  - XML-doc: ссылки на ADR-036 §1.1/§1.2, инвариант `applied + skipped = requested`
    (и `declined + skipped = requested`), смысл `updatedCommodities = 0` (решение состоялось,
    D10 ADR-027), отсутствие `updatedCommodities` у decline (намеренная асимметрия §1.2),
    заполненность полей у `applied` (категория есть, `Reason = null`) и у `skipped`
    (только `Reason`, остальные поля `null`).

Новый файл тестов (`Analytics/tests/ReceiptCollector.Analytics.Api.Tests/`):

- `PrecategorizedBulkModelsTests.cs` - unit без инфраструктуры: дословные wire-имена всех пяти
  констант (литералы в тесте, не ссылки на сами константы), точный состав `All` для исходов и
  причин, точное сравнение (значения с другим регистром, пробелами и `_` вместо `-` не входят
  в множество), `skipped`-строка без категории и без `updatedCommodities`, ноль обновлённых
  товаров у `applied` (решение всё равно состоялось), инварианты агрегатов, отсутствие поля
  `UpdatedCommodities` в контракте отклонения.

Изменён только файл плана (строка статуса задачи 01). ADR, задача, существующие модели, сервисы,
репозитории, эндпоинты, фронтенд, настройки, миграции, Go-сервисы, MongoDB и nginx не тронуты.

## Как проверено

- `cd Analytics && dotnet build` - 0 ошибок, 30 предупреждений (все предсуществующие NU1902/NU1903
  про уязвимые версии транзитивных пакетов, к задаче не относятся)
- `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedBulkModels"` - 16/16 passed
- `cd Analytics && dotnet test` - 798/798 passed, 0 failed, 0 skipped (включая Architecture-тесты
  зависимостей проектов и сквозные тесты на Testcontainers)

## Итоги ревью

### Раунд 1 - APPROVED

Blockers отсутствуют: состав и имена типов соответствуют задаче 01, wire-константы дословно
совпадают с ADR-036 §1.1/§1.2, Application не получил зависимостей от Infrastructure/Api,
существующие сигнатуры не изменены, тесты покрывают требования раздела «Тесты» задачи.

Принято и внедрено одно неблокирующее замечание:

1. В XML-doc `BulkConfirmItemResult` добавлена явная формулировка, что у `applied`-строк
   `CategoryId`/`CategoryName` заполнены фактически применённой категорией из снимка (D1),
   а `Reason = null`; у `skipped`-строк заполнен только `Reason`. Поведение не менялось
   (только doc), после правки тесты и сборка перепрогнаны: 798/798 passed, 0 errors.

### Осознанные решения (зафиксировано явно)

- **Сериализация `null` у `skipped`-строк.** В ADR-036 §1.1 для пропущенной строки показаны
  только `id`/`outcome`/`reason`, а `categoryId`/`categoryName`/`updatedCommodities` не показаны
  вовсе. План задачи 01 предписывает nullable-поля со значением `null` (стиль проекта, прецедент
  `PrecategorizedCommodityDto.MerchantName`), и реализация следует плану: поля присутствуют в
  контракте и сериализуются как `null`, клиент ориентируется на `outcome`. Это осознанный выбор
  в пользу стабильной формы ответа (одна и та же схема строки независимо от исхода) вместо
  «то есть поле, то нет». Задача 04 маппит это в response-записи без изменения семантики.
- **`BulkItemOutcomes.All` объединяет исходы обоих эндпоинтов** (`applied`, `declined`,
  `skipped`) намеренно: это один словарь wire-имён контракта массовых операций, а не перечень
  допустимых значений для конкретного эндпоинта. Ограничение «confirm отдаёт только
  `applied`/`skipped`, decline - только `declined`/`skipped`» задаётся маппингом сервиса
  (задача 03) и проверяется тестами эндпоинтов (задача 04), а не составом множества. Так задача 03
  использует один набор констант, и случайное переименование ловится одним тестом. `All` —
  неизменяемое множество, поэтому расширять/сужать его по месту никто не сможет.
- **Расхождение имён с ADR-036 §4 — в пользу плана.** В таблице §4 ADR упомянуты файл
  `Models/PrecategorizedCommodityModels.cs` и единый `BulkItemResult(...)`, тогда как план задачи 01
  предписывает отдельный файл `PrecategorizedBulkModels.cs` и две строковые модели
  (`BulkConfirmItemResult` с категорией и счётчиком, `BulkDeclineItemResult` без них). Реализация
  следует плану: разделение убирает nullable-поля, которые у decline не имеют смысла, и делает
  асимметрию контракта видимой в типах. ADR в рамках этой задачи **не правился** — расхождение
  вынесено в заметки для коммита; актуализация §4 ADR требует отдельного решения @software-architect.

### Заметки для задач 03 и 04

- Задача 03 собирает `BulkConfirmResult`/`BulkDeclineResult`: порядок `Results` — порядок `ids`
  запроса (сопоставление по id), `requested = ids.Count`, `NotFound`/`Conflict` репозитория
  превращаются в `skipped` с `BulkSkipReasons.NotFound`/`BulkSkipReasons.AlreadyApplied`,
  поля категории и счётчика у `skipped` остаются `null`.
- Задача 04 маппит Application-модели в локальные response-записи модуля; тексты валидации и
  коды `400/401/403/500` определяются там же, models задач 01 wire-имена не меняют.

## Заметки для коммита

Рекомендуемое сообщение коммита:

```text
t-01 PrecategorizedBulk models: bulk confirm/decline results and outcome/reason wire constants
```

На что обратить внимание при ручной проверке:

- в коммит должны попасть только `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/PrecategorizedBulkModels.cs`,
  `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/PrecategorizedBulkModelsTests.cs` и строка
  статуса в `docs/plans/precategorized-bulk-operations.md`; файлы ADR, задачи и плана на момент
  коммита ещё не отслеживаются git — их стоит включить отдельно, если так задумано;
- wire-имена (`applied`, `declined`, `skipped`, `already-applied`, `not-found`) — часть контракта
  с фронтендом и endpoint-тестами: любое переименование ломает задачи 04-08, менять только вместе;
- в этой задаче нет ни кода репозитория, ни сервиса, ни эндпоинтов: если в диффе появились SQL,
  транзакции или HTTP-коды — это чужая задача;
- расхождение имён с ADR-036 §4 (`BulkItemResult` → две модели, файл `PrecategorizedBulkModels.cs`)
  зафиксировано осознанно; при желании исправить §4 ADR — отдельным коммитом после решения
  архитектора.
