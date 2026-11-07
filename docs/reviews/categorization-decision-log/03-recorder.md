# Задача 03 - Recorder: контракт, допустимые источники, best-effort запись

- **Задача**: docs/plans/categorization-decision-log.md#t-03-recorder-контракт-допустимые-источники-best-effort-запись
- **Дата**: 2026-11-07
- **Ревью**: раундов - 1, вердикт APPROVED (блокеров нет)

## Что сделано

Появилась единая точка записи журнала «предложено → принято»: контракт
`ICategorizationDecisionRecorder` в Application и реализация `CategorizationDecisionRecorder`
в Infrastructure. Recorder принимает черновики из save-путей, проверяет форму каждого
(источник — одно из четырёх wire-имён, обе категории — определённые коды, предложенная
не `Undefined`, итоговая `0` допустим; имя непустое и укладывается в `varchar(256)`),
нормализует название через существующий `CommodityNameNormalizer`, проставляет серверное
`CreatedAt` и пишет все события одним `AddRangeAsync` (один `SaveChangesAsync`). Метод
никогда не бросает исключений: невалидные черновики отбрасываются с `Warning` (только
количество), сбой записи логируется как `Error`. Recorder зарегистрирован в DI (scoped).
Точки фиксации в save-путях появятся в T-04…T-06 — в этой задаче их нет.

## Изменённые файлы

**Application**
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/CategorizationDecisions/Models/CategorizationDecisionModels.cs` - новый: `CategorizationDecisionSources` (wire-имена из `CategorizationSourceNames`, неизменяемое `FrozenSet` с `StringComparer.Ordinal`, `IsAllowed` за O(1)) и `CategorizationDecisionDraft`.
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/CategorizationDecisions/Contracts/ICategorizationDecisionRecorder.cs` - новый контракт `RecordAsync`; XML-док фиксирует «не бросает ни при каких обстоятельствах, включая отмену токена».

**Infrastructure**
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/CategorizationDecisions/CategorizationDecisionRecorder.cs` - новый: валидация/фильтрация черновиков, нормализация имени, `TimeProvider.GetUtcNow()`, один батч-INSERT, `catch (Exception)` -> `LogError`; `try` покрывает всё тело метода, включая защиту от `null`-набора.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` - регистрация `AddScoped<ICategorizationDecisionRecorder, CategorizationDecisionRecorder>()` (+ using'и).

**Тесты**
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CategorizationDecisionRecorderTests.cs` - новый, 24 теста (NSubstitute + `FakeTimeProvider`): валидный черновик (нормализация, время, принявший, admin-флаг, `Id`); `AcceptedByAdmin = false`; согласие и `final = Undefined`; все четыре допустимых источника (Theory); недопустимые источники (`existing`, `undefined`, неизвестный, `AI`, пустой, `null`); `suggested = Undefined`/неизвестный код/неизвестный `final`; пустое и пробельное имя; границы 256/257; пустая коллекция, все невалидные и `null`-набор (репозиторий не вызывается); батч из 3 -> ровно один `AddRangeAsync`; одинаковый `CreatedAt` на все события батча; репозиторий бросает `InvalidOperationException` и `OperationCanceledException` -> исключения наружу нет.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CategorizationDecisionRecorderRegistrationTests.cs` - новый wiring-тест DI (scoped + реальный resolve), прецедент T-02.

**Docs**
- `docs/plans/categorization-decision-log.md` - статус T-03 `pending` -> `in-progress` -> `review` -> `approved`.
- `docs/reviews/categorization-decision-log/03-recorder.md` - эта сводка.

## Как проверено

- `cd Analytics && dotnet build` - Build succeeded, 0 errors, 30 warnings (только существующие NU1902/NU1903 о пакетах, были до задачи).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CategorizationDecisionRecorder"` - Passed: 25, Failed: 0 (24 recorder + 1 registration).
- `cd Analytics && dotnet test` - Passed: 476, Failed: 0, Skipped: 0 (57 s). База T-02 - 451 тест, добавлено 25; существующие тесты не менялись.
- Ревьюер независимо прогнал `dotnet build` (0 errors) и `dotnet test` (471 passed / 0 failed на момент ревью) и подтвердил рамки задачи, правила валидации снимка (включая `final = 0`) и контракт «никогда не бросает».

## Итоги ревью

- **Принятые отклонения (зафиксированы, чтобы на приёмке T-12 это не выглядело случайностью):**
  - `CommodityName` события хранится после `Trim()` внешних пробелов; внутренние пробелы
    схлопывает только `CommodityNameNormalizer`. Обоснование: проверяемое значение
    гарантированно укладывается в `varchar(256)`, сочетания-двойники из-за краевых пробелов
    не появляются. Закреплено комментарием у валидации имени в `CategorizationDecisionRecorder`
    и строкой здесь.
  - Один `TimeProvider.GetUtcNow()` на вызов: все события одного батча получают одинаковый
    `CreatedAt` (момент решения). Закреплено тестом-инвариантом.
  - Wiring-тест DI добавлен сверх перечня плана (прецедент T-02: потеря регистрации иначе
    видна только в production).
  - Защита от `null`-набора черновиков сверх перечня плана: без неё `foreach` нарушил бы
    контракт «никогда не бросает». Покрыта тестом.
- **Уточнение по замечанию ревьюера:** английские тексты логов — не отклонение, а стиль
  Infrastructure в проекте; семантика сообщений ADR сохранена (`Warning` — только количество,
  `Information` — число записанных, `Error` — сбой, названия товаров — только `Debug`).
- **Гвард «нормализованное имя <= 256»** оставлен без прямого теста с обоснованием, принятым
  ревьюером: текущая нормализация (lowercase + trim + схлопывание пробелов) на практике не
  удлиняет строку (`'İ'`/U+0130 в .NET 10 invariant не раскрывается), поэтому нормализованное
  имя не может превысить обрезанное; граница длины покрыта тестом 256/257. Гвард остаётся
  защитой от будущих изменений нормализатора.
- **Применённые suggestions ревьюера (дешёвые и безопасные):**
  - `try` покрывает всё тело `RecordAsync`, включая проверку и лог `null`-набора; вне `try`
    осталась только инициализация int-счётчика (не бросает) — контракт покрыт буквально.
  - `CategorizationDecisionSources.All` — неизменяемый `FrozenSet<string>` с
    `StringComparer.Ordinal`; `IsAllowed` — O(1).
  - `Theory` по всем четырём wire-именам источников и тест инварианта «общий `CreatedAt`
    на батч» добавлены.
- **Не применено осознанно:** изменение поведения change tracker после сбоя
  `SaveChangesAsync` — вне рамок T-03 (recorder не владеет scope'ом и не должен менять
  семантику `DbContext`); вынесено предупреждением для T-04…T-06 в «Заметки» ниже.

## Заметки для коммита

- Рекомендуемое сообщение коммита: `categorization decision log recorder`
  (в стиле T-01 `categorization decision log migration` и T-02
  `t-02 domain of categories journal`).
- **Untracked-файлы T-03 — все новые, их нужно добавить в коммит:**
  - `Analytics/src/ReceiptCollector.Analytics.Application/Modules/CategorizationDecisions/Contracts/ICategorizationDecisionRecorder.cs`
  - `Analytics/src/ReceiptCollector.Analytics.Application/Modules/CategorizationDecisions/Models/CategorizationDecisionModels.cs`
  - `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/CategorizationDecisions/CategorizationDecisionRecorder.cs`
  - `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CategorizationDecisionRecorderTests.cs`
  - `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CategorizationDecisionRecorderRegistrationTests.cs`
  (в `git status` две новые директории `Modules/CategorizationDecisions/` показываются
  свёрнуто, но файлов внутри — пять). Плюс изменённый
  `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs`
  и строка статуса в `docs/plans/categorization-decision-log.md`.
- **Не включать** посторонние изменения, существовавшие до задачи:
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
  `appsettings.Development.json`, `wwwroot/index.html`.
- На что обратить внимание при ручной проверке:
  1. `dotnet test` зелёный; новых записей в `categorization_decisions` из save-путей в этом
     коммите ещё нет (это T-04…T-06), поведение API не менялось.
  2. Recorder не бросает: тесты с `InvalidOperationException`, `OperationCanceledException`
     и `null`-набором подтверждают контракт; `final = Undefined` пишется, `suggested = Undefined`
     отбрасывается.
  3. Нормализация — только `CommodityNameNormalizer`; код категорий — `CommodityCategory`,
     имя принявшего не денормализуется.
  4. **Предупреждение для T-04…T-06 (change tracker):** `DbContext` scoped на запрос; после
     неудачного `SaveChangesAsync` в recorder'е добавленные сущности остаются в change tracker.
     Если в том же scope после recorder'а появится ещё один `SaveChangesAsync`, он повторит
     (или задублирует по PK) вставку журнала. Recorder должен оставаться последней
     DB-операцией запроса; при необходимости защиты — detach добавленных сущностей в `catch`
     отдельной задачей, не в T-03.
