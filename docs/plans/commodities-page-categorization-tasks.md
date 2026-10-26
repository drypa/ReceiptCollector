# Декомпозиция: категоризация страницы «Товары» (подзадачи для команды)

> Источники: задача [commodities-page-categorization](../tasks/commodities-page-categorization.md)
> (C1–C11, критерии приёмки, edge cases), [ADR-028](../adr/028-commodities-page-categorization.md)
> (основной документ реализации), план [commodities-page-categorization](commodities-page-categorization.md)
> (шаги 1–9), ADR 009/019/020/021/026/027.
>
> **Этот документ — только декомпозиция.** Новых архитектурных решений он не содержит и не заменяет
> ADR-028. Всё, что ниже, — порядок работ, файлы, зависимости и проверки. Спорные места вынесены в
> раздел «Открытые вопросы (эскалация к @software-architect)» и **не должны решаться исполнителем
> самостоятельно**.
>
> Правки плана: 9 шагов проверены на полноту. Порядок скорректирован: (1) рефакторинг resolver
> выполняется **до** batch-сервиса и оформлен отдельной регрессионной подзадачей по категоризации чека;
> (2) исправление дефекта `useCommodities` вынесено в отдельную подзадачу и делается **до** страницы;
> (3) добавлена подзадача точечного повтора строки (требование C2 и ручная приёмка №10 — в шагах 7–8
> плана UI не был описан); (4) документация по env-переменной разбита: проброс в compose/.env — в шаге
> конфигурации (T-02), строка в `AGENTS.md` — в подзадаче документации (T-13).

## Обозначения

| Поле | Значение |
|---|---|
| Приоритет | **P0** — без этого нет сквозного сценария; **P1** — требуется критериями приёмки; **P2** — качество/документация, не блокирует |
| Оценка | человеко-дни одного разработчика, без учёта ревью и ручной приёмки |
| Зависит от | подзадачи, которые должны быть **влиты** до старта |
| Проверка | конкретные команды и/или ручные сценарии |

Общие команды (из плана, раздел 4):

```bash
cd Analytics && dotnet test              # все тесты, включая Architecture/ProjectDependencyTests
cd Analytics && dotnet build
cd Analytics/frontend && npm run lint && npm run build
```

Общий критерий готовности любой бэкенд-подзадачи: `cd Analytics && dotnet test` зелёный **без правок
существующих тестов** (кроме случаев, явно оговорённых в подзадаче), новые тесты покрывают поведение,
описанное в ADR-028.

## Сводка и порядок исполнения

| № | Подзадача | Слой | Приор. | Оценка | Зависит от | Параллельно с |
|---|---|---|---|---|---|---|
| T-01 | Домен: исключение + owner-scoped чтение/пакетная запись в репозитории | Domain/Infra | P0 | 1.0 | — | T-02, T-06 |
| T-02 | Конфигурация бюджета запроса `CommodityBatchCategorization:SuggestTimeout` | Infra/Deploy | P1 | 0.25 | — | T-01, T-06 |
| T-03 | Вынос конвейера категоризации в `ICommodityCategoryResolver` (рефакторинг без изменения поведения) | App/Infra | P0 | 1.0 | — | T-02, T-06 |
| T-04 | **Регрессия категоризации чека** после выноса resolver (гейт) | тесты | P0 | 0.25 | T-03 | — |
| T-05 | Batch-сервис: модели, контракт, `SuggestAsync`/`ApplyAsync`, DI, тесты | App/Infra | P0 | 1.5 | T-01, T-02, T-03, T-04 | T-06 |
| T-06 | Общий helper `AdminAuthorization` (`401`/`403`) + перевод `PrecategorizedCommodityEndpoints` | Api | P1 | 0.25 | — | T-01, T-02, T-03, T-05 |
| T-07 | API: два эндпоинта `/api/commodities/categories*` + регистрация, тесты хендлеров | Api | P0 | 1.0 | T-05, T-06 | — |
| T-08 | Frontend: типы, API-клиент, вынос `getSuggestionNote` в `utils/` | FE | P1 | 0.5 | T-07 (контракт) | T-09, T-11 |
| T-09 | **Дефект `useCommodities`**: clamp без перезапроса → нормализация пустой страницы | FE | P1 | 0.5 | — | T-08, T-11 |
| T-10 | Frontend: машина состояний `useCommodityPageCategorization` (порции, дедлайн, `touched`, abort, `generation`) | FE | P0 | 1.5 | T-08 | T-09, T-11 |
| T-11 | `Pagination`: аддитивный проп `disabled` | FE | P2 | 0.1 | — | любые |
| T-12 | Frontend: страница, таблица (режим проверки), блокировки, тосты, стили | FE | P0 | 1.5 | T-09, T-10, T-11 | — |
| T-13 | Frontend: точечный повтор строки («Повторить») + `beforeunload` (опц.) | FE | P1 | 0.25 | T-10, T-12 | — |
| T-14 | Документация: `AGENTS.md` (env-переменная), ссылки в ADR/плане | docs | P2 | 0.1 | T-02 | любые после T-02 |
| T-15 | Финальная проверка: полный прогон тестов, сборка/линт фронта, ручная приёмка | все | P0 | 0.5–1.0 | T-01…T-14 | — |

**Критический путь:** T-01 → T-05 → T-07 → T-08 → T-10 → T-12 → T-13 → T-15
(плюс T-03 → T-04 перед T-05; T-06 перед T-07; T-02 перед T-05; T-09 и T-11 — попутно).

Суммарно последовательно ≈ 10.5 чел.-дн.; при распараллеливании по двум исполнителям (backend / frontend)
≈ 6–7 рабочих дней до ручной приёмки.

---

## T-01. Домен и репозиторий: owner-scoped чтение и пакетная запись

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** — · **Параллельно:** T-02, T-03, T-06

### Цель
Дать инфраструктуре возможность читать подмножество товаров **текущего пользователя** по списку id и
атомарно записывать пакет категорий, не полагаясь на глобальный query-filter (в `CommodityRepository`
он не активен — ADR-028, «Текущее состояние кода»).

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommoditiesNotAccessibleException.cs` — новый (по образцу
  `Domain/Modules/Receipts/ReceiptNotFoundException.cs`; в тексте — только количество недоступных id, без самих id).
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/ICommodityRepository.cs` — + два метода и XML-doc.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityRepository.cs` — реализация.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityBatchRepositoryTests.cs` — новый (EF InMemory,
  по образцу `CommodityReadServiceFilterTests`).

### Чек-лист
- [ ] `CommoditiesNotAccessibleException` создан; сообщение содержит **количество** недоступных id.
- [ ] `ICommodityRepository.GetByOwnerAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct)`
      → `Task<IReadOnlyList<Commodity>>`.
- [ ] `ICommodityRepository.UpdateCategoriesAsync(Guid userId, IReadOnlyDictionary<Guid, CommodityCategory> changes, CancellationToken ct)`
      → `Task<int>` (число **фактически изменённых** строк).
- [ ] XML-doc обоих методов содержит предупреждение: явный `c.Receipt.UserId == userId` обязателен,
      глобальный query-filter на `Receipts` в `CommodityRepository` не активен.
- [ ] `GetByOwnerAsync`: `AsNoTracking()`, `ids.Contains(c.Id) && c.Receipt.UserId == userId`,
      маппинг в доменный `Commodity` — как в существующем `GetByIdAsync` (включая `Category` из `CategoryId`/`CategoryName`).
- [ ] `GetByOwnerAsync` с неизвестным/чужим id возвращает **частичный** результат без исключения.
- [ ] `UpdateCategoriesAsync`: один запрос сущностей по id + owner-фильтр; если найдено меньше, чем
      запрошено → `CommoditiesNotAccessibleException` **до** любых мутаций (ничего не записано).
- [ ] Для каждой найденной строки сравнение текущего `CategoryId` с новым; изменённые получают
      `CategoryId = (int)category` и `CategoryName = CommodityCategoryHelper.GetDisplayName(category)`.
- [ ] Ровно **один** `SaveChangesAsync` на вызов; возвращается число изменённых строк.
- [ ] Существующие методы (`GetByIdAsync`, `UpdateCategoryAsync`) не изменены (их используют чек и `CommodityEndpoints`).
- [ ] Тесты по плану 10.1 (пункты 1–6) написаны и зелёные.

### Критерий готовности
Оба метода доступны через интерфейс, owner-scoping проверен тестами, пакетная запись атомарна
(при недоступном id — исключение и ноль изменений), существующие тесты не тронуты.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityBatchRepository"
cd Analytics && dotnet test
```

---

## T-02. Конфигурация бюджета запроса `CommodityBatchCategorization:SuggestTimeout`

**Приоритет:** P1 · **Оценка:** 0.25 чел.-дн. · **Зависит от:** — · **Параллельно:** T-01, T-03, T-06

### Цель
Ввести независимый от `AI:Timeout` бюджет одного suggest-запроса страницы (по умолчанию 25 с, ADR-028 п. 3)
и пробросить его через env по образцу ADR-021.

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/CommodityBatchCategorizationOptions.cs` — новый
  (`TimeSpan SuggestTimeout = TimeSpan.FromSeconds(25)`), секция `CommodityBatchCategorization`.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` —
  `AddOptions<CommodityBatchCategorizationOptions>().Bind(...).Validate(o => o.SuggestTimeout > TimeSpan.Zero, ...).ValidateOnStart()`
  (регистрация resolver/сервиса — в T-03/T-05).
- `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` — `"CommodityBatchCategorization": { "SuggestTimeout": "00:00:25" }`.
- `docker-compose.yml` — `CommodityBatchCategorization__SuggestTimeout=${CommodityBatchCategorization__SuggestTimeout:-00:00:25}`
  рядом с блоком `Precategorization__*`.
- `.env.example` — закомментированный блок с пояснением (бюджет одного suggest-запроса страницы; должен быть
  существенно меньше `proxy_read_timeout` nginx = 120 с).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityBatchCategorizationOptionsTests.cs` — новый
  (по образцу `PrecategorizationOptionsTests.cs`).

### Чек-лист
- [ ] Опция с дефолтом `00:00:25`; секция биндится, значения из env (`CommodityBatchCategorization__SuggestTimeout`) читаются.
- [ ] `ValidateOnStart`: `SuggestTimeout <= 0` → ошибка старта (тест).
- [ ] `appsettings.json` содержит секцию; значение совпадает с дефолтом.
- [ ] `docker-compose.yml`: переменная проброшена в сервис `analytics` (окружение API); комментарий в стиле соседних строк.
- [ ] `.env.example`: закомментированный блок с пояснением и связью с `proxy_read_timeout`.
- [ ] `AGENTS.md` **в этой подзадаче не правится** — строка таблицы настроек делается в T-14 (чтобы не дублировать шаг 2 и шаг 9 плана).

### Критерий готовности
Опция читается из конфигурации и env, валидируется на старте, дефолт 25 с, документация проброса есть.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityBatchCategorizationOptions"
cd Analytics && dotnet build
docker compose config | grep CommodityBatchCategorization   # переменная видна в окружении сервиса
```

---

## T-03. Вынос конвейера категоризации в `ICommodityCategoryResolver`

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** — · **Параллельно:** T-02, T-06
**Блокер:** открытый вопрос №1 (конструктор `CommodityCategorizationService` и инвариант «тесты без изменений»).

### Цель
Один конвейер `existing → cache → precategorized → ai` (дедупликация по нормализованному имени,
обработка ошибок/таймаутов ИИ, `aiEnabled`) — в отдельном resolver'е, которым пользуются и suggest чека,
и suggest порции. Поведение suggest чека **не меняется**.

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategoryResolver.cs` — новый
  (`ResolveAsync(items, aiEnabled, aiCancellationToken, cancellationToken)`).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs` — новый
  (перенос шагов 1–4 из `CommodityCategorizationService.SuggestForReceiptAsync` **без изменения порядка и семантики**).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` —
  делегирование resolver'у; `ApplyConfirmedCategoriesAsync` не меняется (включая reset-семантику и `TryAdd`).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` — регистрация resolver (Scoped).

### Чек-лист
- [ ] Контракт `ICommodityCategoryResolver` в Application; реализация — Infrastructure (проверяется `Architecture/ProjectDependencyTests`).
- [ ] Resolver воспроизводит шаги 1:1: `existing` (`Category is { Id: not 0 }`) → `cache`
      (`CommodityNameNormalizer` + дисплейное имя из `CommodityCategoryHelper`) → `precategorized`
      (**один** batch-запрос по точным именам, ошибка чтения → Warning + деградация) → `ai`
      (дедуп по нормализованному имени, `SemaphoreSlim` на `AI:Concurrency`, `AiClientException` по позиции → `undefined` + текст).
- [ ] Тексты ошибок не изменены: «AI категория не определена.» для пустого результата ИИ и прежние тексты `AiClientException`-ветки.
- [ ] `aiEnabled == false` → вызовы ИИ не выполняются, остаток помечается `undefined` + «ИИ не настроен.».
- [ ] `OperationCanceledException` от вызова ИИ: если отменён `cancellationToken` (запрос клиента) — пробросить;
      иначе (дедлайн `aiCancellationToken`) — `undefined` + «ИИ не ответил за отведённое время.».
- [ ] `CommodityCategorizationService` зависит от resolver'а; `SuggestForReceiptAsync` = загрузка чека →
      `ResolveAsync(items, aiEnabled: BaseUrl не пуст, aiCancellationToken: cancellationToken, cancellationToken)`.
- [ ] `ApplyConfirmedCategoriesAsync` побайтово эквивалентен прежнему поведению (не тронут).
- [ ] `SemaphoreSlim` создаётся на экземпляр resolver'а (Scoped), как было на scoped-сервисе — публичное поведение не меняется.
- [ ] DI: resolver зарегистрирован Scoped; разрешение `ICommodityCategorizationService` работает.

### Критерий готовности
Функциональность чека идентична прежней; регрессионная сетка `CommodityCategorizationServiceTests`
компилируется и зелёная; новых точек поведения нет.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategorizationService"
cd Analytics && dotnet test
```
Правило ревью: в diff **не должно быть** изменений утверждений тестов `CommodityCategorizationServiceTests`.
Если без правки конструктора/фабрики тесты не компилируются — остановиться и эскалировать (см. открытый вопрос №1).

---

## T-04. Регрессия существующей категоризации чека (обязательный гейт)

**Приоритет:** P0 · **Оценка:** 0.25 чел.-дн. · **Зависит от:** T-03

### Цель
Зафиксировать, что после выноса конвейера в resolver сценарий «Детали чека» не изменился (ADR-028, риск
«Регрессия suggest чека при выносе алгоритма в resolver»; C11 — страницу чека не трогаем).

### Затрагиваемые файлы
- Изменений в коде нет. Проверяются:
  `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs`,
  `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategorizationService.cs`,
  `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategorizationServiceTests.cs`,
  `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCategorizationEndpointsTests.cs`.

### Чек-лист
- [ ] `git diff` по `ReceiptCategorizationEndpoints.cs` **пуст** (эндпоинты чека, включая `503` при пустом
      `AI:BaseUrl` и reset-семантику сохранения, не изменены).
- [ ] `git diff` по `CommodityCategorizationServiceTests.cs` — только механическая правка фабрики
      создания сервиса (если разрешена открытым вопросом №1); утверждения не менялись.
- [ ] Все 20 существующих кейсов `CommodityCategorizationServiceTests` зелёные.
- [ ] `ReceiptCategorizationEndpointsTests` зелёные (в т.ч. `503` при пустом `AI:BaseUrl`, reset при `category: null`).
- [ ] Порядок шагов и тексты ошибок сверены с ADR-009/027 по коду resolver'а (ревью-чеклист T-03), расхождений нет.
- [ ] Ручной smoke на dev: «Детали чека» → «Категоризировать» → предложения приходят; сохранение с пустым
      значением по-прежнему сбрасывает категорию (не как на странице «Товары»).

### Критерий готовности
Письменное заключение в PR: «поведение чека не изменилось», подтверждённое зелёными тестами без правок
утверждений и пустым diff'ом по эндпоинтам чека.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategorizationService"
cd Analytics && dotnet test --filter "FullyQualifiedName~ReceiptCategorizationEndpoints"
git diff --stat -- Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs
git diff --stat -- Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategorizationService.cs
```

---

## T-05. Batch-сервис: модели, контракт, `SuggestAsync`/`ApplyAsync`, DI

**Приоритет:** P0 · **Оценка:** 1.5 чел.-дн. · **Зависит от:** T-01, T-02, T-03, T-04 · **Параллельно:** T-06

### Цель
Сервис, который по подмножеству id товаров пользователя отдаёт предложения (порция, дедлайн, `aiAvailable`,
`aiEnabled = false` без 503) и атомарно применяет assign-only изменения с подсчётом фактических изменений
и пополнением кэша (ADR-028 п. 1.1, 1.2, 3, 4, 8).

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CommodityBatchCategorizationModels.cs` — новый
  (`CommodityBatchCategorizationResult(Items, AiAvailable)`, `CommodityCategoryChange(Guid CommodityId, CommodityCategory Category)`).
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityBatchCategorizationService.cs` — новый.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityBatchCategorizationService.cs` — новый.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` — `AddScoped<ICommodityBatchCategorizationService, ...>`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityBatchCategorizationServiceTests.cs` — новый.

### Чек-лист

Модели и контракт:
- [ ] Элемент результата — **существующий** `ReceiptItemCategorizationResult` (переиспользование, wire-формат идентичен suggest чека).
- [ ] `SuggestAsync(Guid userId, IReadOnlyCollection<Guid> commodityIds, CancellationToken ct)`.
- [ ] `ApplyAsync(Guid userId, IReadOnlyCollection<CommodityCategoryChange> changes, CancellationToken ct)` → `int`.

`SuggestAsync`:
- [ ] `GetByOwnerAsync(userId, ids.Distinct())`; ненайденные id — Warning **только количеством** (без названий/id).
- [ ] Строки с пустым/пробельным названием не уходят в resolver: сразу `undefined` + «Название товара не заполнено.»
      (поведение suggest чека не меняется).
- [ ] `> 20` уникальных названий в порции → `ArgumentException` (эндпоинт мапит в `400`).
- [ ] `aiEnabled = !string.IsNullOrWhiteSpace(aiOptions.Value.BaseUrl)`; при `false` resolver не вызывает ИИ, `AiAvailable = false`.
- [ ] Дедлайн через `TimeProvider`: `CancellationTokenSource.CreateLinkedTokenSource(ct)` + отмена по
      `SuggestTimeout`; отмена запроса клиентом **не** маскируется (пробрасывается `OperationCanceledException`).
- [ ] Ответ — `200`-совместимый частичный результат уже полученных названий; незавершённые → `undefined` + текст таймаута.
- [ ] Логи по ADR-028 п. 11: Debug — размер порции, длительность, распределение `source`, число ошибок/таймаутов,
      `aiAvailable`, число пропущенных id; Warning — пропущенные id (количество), таймаут бюджета. Названия — **только** Debug.
- [ ] Suggest **ничего не пишет**: ни `UpdateCategoriesAsync`, ни `TryAdd`, ни записи/смены статуса в `precategorized_commodities`.

`ApplyAsync`:
- [ ] Изменение с `Undefined` → `ArgumentException` **до** обращения к репозиторию (защита в сервисе, не только в эндпоинте).
- [ ] Имена для кэша берутся через `GetByOwnerAsync(userId, changedIds)` **до** записи (без второго `SaveChanges`;
      контракт репозитория из ADR-028 не расширяется). Если набор неполный — `CommoditiesNotAccessibleException`
      до записи (см. открытый вопрос №3).
- [ ] `UpdateCategoriesAsync(userId, dict, ct)` → `updated`; исключение репозитория пробрасывается наружу.
- [ ] После успешной фиксации: `TryAdd(CommodityNameNormalizer.NormalizeName(name), category)` для **каждой изменённой** строки
      (first-wins; no-op при «замороженном» кэше); при `updated = 0` — ни одного `TryAdd`.
- [ ] В `precategorized_commodities` не пишет (только чтение через resolver при suggest).
- [ ] DI-регистрация Scoped.

Тесты (план 10.2, пункты 1–17):
- [ ] Все 17 сценариев написаны и зелёные; используются NSubstitute + EF InMemory/NSubstitute,
      `FakeTimeProvider` для дедлайна (без реальных задержек).

### Критерий готовности
Сервис реализует контракт ADR-028 полностью, покрыт тестами 10.2, не пишет в очередь предкатегорий,
кэш пополняется только при сохранении.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityBatchCategorizationService"
cd Analytics && dotnet test
```

---

## T-06. Общий helper `AdminAuthorization` (`401`/`403`)

**Приоритет:** P1 · **Оценка:** 0.25 чел.-дн. · **Зависит от:** — · **Параллельно:** T-01…T-05

### Цель
Третьей копии проверки прав не создавать (ADR-028 п. 10): вынести `AuthorizeAdminAsync` из
`PrecategorizedCommodityEndpoints` в общий helper и переиспользовать в новом модуле. Поведение
`/api/precategorized-commodities` не меняется.

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Users/AdminAuthorization.cs` — новый.
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs` — удалить приватный метод, перейти на helper.

### Чек-лист
- [ ] Helper переносит тело и XML-doc **дословно**, включая объяснение, почему `Results.Forbid()` даёт `500`
      (нет схемы аутентификации) и почему 403 отдаётся явным `Results.StatusCode(StatusCodes.Status403Forbidden)`.
- [ ] Сигнатура допускает вызов из статических хендлеров minimal API (`HttpContext`/`UserContext` + `IUserRepository`).
- [ ] `PrecategorizedCommodityEndpoints` использует helper; коды ответов не изменились.
- [ ] `MerchantEndpoints` **не трогается** (его `Forbid` — отдельный бэклог).
- [ ] `PrecategorizedCommodityEndpointsTests` зелёные **без правок**.

### Критерий готовности
Один источник проверки прав; поведение существующего админского модуля идентично.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedCommodityEndpoints"
cd Analytics && dotnet test
```

---

## T-07. API: `POST /api/commodities/categories/suggest` и `PUT /api/commodities/categories`

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-05, T-06

### Цель
Два stateless-эндпоинта с валидацией и правами строго по ADR-028 п. 1.1/1.2/10.

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Commodities/CommodityBatchCategorizationEndpoints.cs` — новый.
- `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs` — `app.MapCommodityBatchCategorizationEndpoints();`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityBatchCategorizationEndpointsTests.cs` — новый
  (по образцу `PrecategorizedCommodityEndpointsTests` / `ReceiptCategorizationEndpointsTests`:
  `UserContext.SetUserId`, исполнение `IResult` на `DefaultHttpContext`, проверка кодов **исполнением**).

### Чек-лист
Suggest (`POST /categories/suggest`, тело `{ commodityIds }`):
- [ ] Порядок проверок: `401` (нет `UserContext.UserId`) → `403` (не админ, явный `StatusCode`) → валидация → сервис.
- [ ] `400`: `commodityIds` пуст/отсутствует; `> 200`; `Guid.Empty`; дубликаты. Тексты — английские, текстовые тела,
      без ProblemDetails (эталон ADR-027 п. 7).
- [ ] `ArgumentException` из сервиса (`> 20` уникальных названий) → `400`.
- [ ] Успех и частичный результат → `200 { items, aiAvailable }`; **пустой `AI:BaseUrl` → `200`, не `503`**.
- [ ] Неизвестные/чужие id → `200` и просто отсутствуют в `items`.
- [ ] Тег группы `Commodities`, XML-doc со ссылкой на ADR-028.

Save (`PUT /categories`, тело `{ items: [{ commodityId, category }] }`):
- [ ] Порядок проверок: `401` → `403` → валидация → фильтрация «не трогаем» → сервис.
- [ ] `400`: `items` пуст/отсутствует; `Guid.Empty`; дубликат `commodityId`; `category` — число (`int.TryParse`),
      неизвестное имя, `Undefined`. Парсинг — зеркально `ReceiptCategorizationEndpoints.Save`
      (`Enum.TryParse(ignoreCase: true)`, `Enum.IsDefined`), регистр принимается (`"dairy"`).
- [ ] `category == null` / `""` / `"   "` → элемент **пропускается**; если пропущены все → `200 { updated = 0 }`
      **без вызова сервиса**.
- [ ] Успех → `200 { updated }` из сервиса (значение сервера, не пересчёт на клиенте).
- [ ] `CommoditiesNotAccessibleException` → `409` с текстом «Некоторые строки больше недоступны. Обновите страницу и повторите сохранение.»
      (по-английски, как остальные тексты API).
- [ ] Оба обработчика — `public static async Task<IResult>` (тестируемость уровня хендлера).
- [ ] Регистрация в `Program.cs` рядом с `MapCommodityEndpoints()`.

Тесты (план 10.3, пункты 1–16):
- [ ] Все 16 сценариев зелёные, включая «403 — это 403, а не 500» через исполнение `IResult`.
- [ ] `Architecture/ProjectDependencyTests` зелёные.

### Критерий готовности
Контракты соответствуют ADR-028 дословно (коды, тела, семантика «не трогаем»); не-админ получает `403`
и при прямом вызове API; `503` на новом эндпоинте невозможен.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityBatchCategorizationEndpoints"
cd Analytics && dotnet test --filter "FullyQualifiedName~ProjectDependencyTests"
cd Analytics && dotnet test
# smoke на dev (после T-15 стенда):
curl -i -X POST http://localhost/api/commodities/categories/suggest -H 'Content-Type: application/json' -d '{"commodityIds":[]}'
```

---

## T-08. Frontend: типы, API-клиент, общий модуль подписей источника

**Приоритет:** P1 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** T-07 (финальная сверка контракта) · **Параллельно:** T-09, T-11

### Цель
Типизированный доступ к новым эндпоинтам и единый источник подписей `source` для страницы «Товары» и
«Деталей чека» (C8; сейчас `getSuggestionNote` живёт локально в `ReceiptDetails.tsx`).

### Затрагиваемые файлы
- `Analytics/frontend/src/types/commodity.ts` — `CategorizationSource`, `CommodityCategorizationItem`, `CommodityCategoriesSuggestion`.
- `Analytics/frontend/src/api/commodities.ts` — `suggestCommodityCategories`, `saveCommodityCategories`, ошибка со `status`.
- `Analytics/frontend/src/utils/categorizationSource.ts` — новый (`getSuggestionNote`).
- `Analytics/frontend/src/components/ReceiptDetails.tsx` — только импорт утилиты (поведение не меняется).

### Чек-лист
- [ ] `CategorizationSource = 'existing' | 'cache' | 'precategorized' | 'ai' | 'undefined'`; в элементе ответа
      `source` типизирован как `CategorizationSource | string` (устойчивость к аддитивным значениям сервера).
- [ ] `CommodityCategorizationItem { commodityId; name; categoryId; categoryName; source; error }` — точно по wire-формату ADR-028 п. 1.1.
- [ ] `CommodityCategoriesSuggestion { items; aiAvailable }`.
- [ ] `suggestCommodityCategories(commodityIds: string[], signal?: AbortSignal)` — `POST`, `credentials: 'include'`, `AbortSignal` проброшен.
- [ ] `saveCommodityCategories(items: Array<{ commodityId: string; category: string | null }>)` — `PUT`, тело как в ADR-028 п. 1.2.
- [ ] Оба метода при `!response.ok` бросают типизированную ошибку с `status` (интерфейс/класс `ApiRequestError`),
      чтобы страница различала `401/403/409/сеть`. Пользовательские сообщения — русские, формируются на клиенте.
- [ ] `getSuggestionNote` перенесён **дословно** («уже присвоена» / «по прошлым чекам» / «из очереди предкатегорий» /
      «предложено ИИ» / «не определена», `default → null`); `ReceiptDetails.tsx` импортирует его вместо локальной копии.
- [ ] Старые методы `fetchCommodities`/`fetchCategories`/`updateCommodityCategory` не изменены по поведению.

### Критерий готовности
Типы и клиент готовы для хука T-10; `ReceiptDetails` работает как раньше; сборка и линт чистые.

### Проверка
```bash
cd Analytics/frontend && npm run build
cd Analytics/frontend && npm run lint
```

---

## T-09. Исправление существующего дефекта `useCommodities` (clamp без перезапроса)

**Приоритет:** P1 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** — · **Параллельно:** T-08, T-10, T-11

### Цель
Закрыть требование C6 и зафиксированный дефект: сейчас хук вычисляет `finalPage` и пишет его в
`currentPage`, но **не перезапрашивает** страницу → после сохранения под фильтром «Без категории»
таблица остаётся пустой на «неправильной» странице (ADR-028, «Текущее состояние кода»).

### Затрагиваемые файлы
- `Analytics/frontend/src/hooks/useCommodities.ts`.

### Чек-лист
- [ ] Инвариант: текущая страница **непустая** и `currentPage <= totalPages`, если `totalItems > 0`.
- [ ] Если загрузка вернула пустой массив, а `requestedPage > computedTotalPages` → **догрузить** `computedTotalPages`
      (не просто записать в state), не более **одной** повторной попытки (защита от цикла; счётчик-guard в ref).
- [ ] Если `totalItems === 0` → пустой список показывается штатно («Товары не найдены»), без догрузок и без ошибки.
- [ ] Догрузка выполняется тем же `loadPage` с тем же `AbortController`-контрактом; отмена предыдущего запроса сохраняется.
- [ ] `isLoading` не «залипает»: `finally`-ветка остаётся корректной для цепочки из двух запросов.
- [ ] Устранён двойной первичный запрос: сейчас два `useEffect` вызывают `loadPage(1)` на монтировании —
      свести к одному (не менять поведение сброса на 1 при смене `pageSize`).
- [ ] `refresh()` по-прежнему перезагружает текущую (уже нормализованную) страницу.
- [ ] Хук не используется на странице «Чеки» (`useReceipts` отдельный) — регрессии там нет; проверить `Routes`.
- [ ] Счётчик `totalItems` берётся из `X-Total-Count` (уже так) и обновляется после `refresh()`.

### Критерий готовности
Пустая последняя страница под фильтром «Без категории» после сохранения автоматически заменяется предыдущей
доступной; пустой список не показывается ошибкой.

### Проверка
Автотестов фронтенда в проекте нет (ADR 011 E1), поэтому:
```bash
cd Analytics/frontend && npm run build && npm run lint
```
Ручной сценарий (выполняется здесь, повторно — в T-15): фильтр «Без категории», размер 5, дойти до последней
страницы, разметить все строки → сохранить → страница подгружает предыдущую доступную, «Найдено товаров»
уменьшилось, пустой таблицы нет.

---

## T-10. Frontend: машина состояний `useCommodityPageCategorization`

**Приоритет:** P0 · **Оценка:** 1.5 чел.-дн. · **Зависит от:** T-08 · **Параллельно:** T-09, T-11

### Цель
Одна машина состояний страницы (`idle | review | saving`): снапшот страницы, черновик, последовательные
порции по 5 названий, прогресс, `touched`, `aiWarning`, отмена/сохранение в любой момент (ADR-028 п. 2, 5).

### Затрагиваемые файлы
- `Analytics/frontend/src/hooks/useCommodityPageCategorization.ts` — новый.

### Чек-лист
Состояние и API хука:
- [ ] `phase: 'idle' | 'review' | 'saving'`; `draft: Record<commodityId, { value; initialValue; source?; error?; touched }>`;
      `progress: { processed; total }`; `aiWarning: boolean`.
- [ ] `value` — **ключ** категории (`Category.key`, имя enum); пустая строка = «не трогаем» (C4).
- [ ] Возвращается: `start(rows)`, `setValue(id, value)`, `save()`, `cancel()`, `retryRow(id)`, `phase`, `draft`,
      `progress`, `aiWarning`, `isDirty`/`changedCount`.

`start(rows)`:
- [ ] Фиксируется **снапшот** `rows`; в режиме проверки список не перезапрашивается (ADR-028 п. 7).
- [ ] `candidates = rows.filter(r => r.categoryId == null || r.categoryId === 0)` (строки без содержательной категории).
- [ ] `initialValue`: для содержательной категории — `key` из справочника (`categoryId → key`), `source = 'existing'`,
      в порции **не участвует**; иначе `value = initialValue = ''`.
- [ ] Группировка кандидатов по **точному** названию; порции по `PAGE_SUGGEST_CHUNK_SIZE = 5` названий;
      в запрос уходят `commodityId` **всех** строк этих названий (строк в порции может быть больше пяти).
- [ ] Запросы строго последовательные (один in-flight), `AbortController` на порцию.
- [ ] Применение ответа по `commodityId` (порядок не гарантирован): `touched`-строки **не перезаписываются**
      (подпись «изменено вручную»), остальные получают `value = key(categoryId) ?? ''` (для `undefined` — пусто),
      `source`, `error`.
- [ ] `progress.processed` увеличивается на число строк завершившейся порции; `total = candidates.length`.
- [ ] `aiAvailable === false` хотя бы в одной порции → `aiWarning = true` (постоянное предупреждение, сбрасывается только `cancel`/`save`).
- [ ] Монотонный `generation`-счётчик: ответы после `cancel()`/`save()`/unmount игнорируются (StrictMode/гонки).
- [ ] Нормализация названий на клиенте **не дублируется** (только точное совпадение).

`setValue` / `retryRow`:
- [ ] `setValue(id, value)` → `value`, `touched = true`, `source` помечается «изменено вручную».
- [ ] `retryRow(id)` → тот же suggest-эндпоинт с одним `commodityId` (C2, ADR-028 п. 1.3); строка не должна быть
      `touched` с непустым значением (иначе повтор затрёт правку); ошибка порции не ломает остальные строки
      и не сбрасывает черновик.

`save()`:
- [ ] `phase = 'saving'`, текущая порция прерывается (`abort`).
- [ ] `changed` = строки, где `value !== initialValue` **и** `value !== ''`; только они уходят в `PUT`.
- [ ] `changed` пуст → выйти из режима, `toast.success('Сохранено категорий: 0')` (ADR-020: `N = 0` — тост показывается), без запроса.
- [ ] Успех → `phase = 'idle'`, `onSaved(updated)` (страница делает `refresh()` и тост).
- [ ] Ошибка → `phase = 'review'`, черновик **сохраняется**, тост с русским текстом по `status`
      (`409` — «Часть строк больше недоступна, обновите страницу»; `403` — «Недостаточно прав»; иначе — текст ошибки/дефолт).
- [ ] `cancel()` → `abort`, `phase = 'idle'`, черновик очищается; в БД ничего не пишется.

### Критерий готовности
Хук полностью описывает цикл «запуск → порции → правка → сохранение/отмена», не теряет введённое,
не применяет устаревшие ответы, не пишет ничего самостоятельно.

### Проверка
```bash
cd Analytics/frontend && npm run build && npm run lint
```
Ручная проверка в связке с T-12 (см. T-15, пункты 2–7, 10).

---

## T-11. `Pagination`: аддитивный проп `disabled`

**Приоритет:** P2 · **Оценка:** 0.1 чел.-дн. · **Зависит от:** — · **Параллельно:** любые

### Затрагиваемые файлы
- `Analytics/frontend/src/components/Pagination.tsx`.

### Чек-лист
- [ ] `disabled?: boolean` в пропсах; все кнопки («Назад», номера, «Вперёд») получают `disabled`, `aria-disabled`
      не конфликтует с существующим `disabled` по границам страниц.
- [ ] При `disabled` клики не вызывают `onPageChange`/`onNext`/`onPrevious`.
- [ ] Поведение без пропа идентично прежнему (страница «Чеки» и `MerchantsPage` не затронуты).

### Проверка
```bash
cd Analytics/frontend && npm run build && npm run lint
```

---

## T-12. Frontend: страница, таблица (режим проверки), блокировки, тосты

**Приоритет:** P0 · **Оценка:** 1.5 чел.-дн. · **Зависит от:** T-09, T-10, T-11

### Затрагиваемые файлы
- `Analytics/frontend/src/components/CommoditiesPage.tsx`.
- `Analytics/frontend/src/components/CommodityTable.tsx`.
- `Analytics/frontend/src/App.css` (только если не хватает переиспользуемых классов).

### Чек-лист

`CommoditiesPage.tsx`:
- [ ] `candidateCount = data.filter(r => r.categoryId == null || r.categoryId === 0).length`.
- [ ] Кнопка «Категоризировать страницу ({candidateCount})» рендерится только при `isAdmin`;
      `disabled` при `candidateCount === 0 || phase !== 'idle'`; `title` «На этой странице нет товаров без категории» при нуле.
- [ ] В режиме проверки вместо неё — «Сохранить категории» (активна и во время порций, кроме `phase === 'saving'`)
      и «Отмена»; строка прогресса «Определяем категории… X из Y».
- [ ] Inline-предупреждение при `aiWarning` с `role="status"` (постоянное, не тост) — ADR-028 п. 4.
- [ ] `controlsLocked = phase !== 'idle'`: `disabled` на селекте размера страницы, селекте фильтра, «Обновить»,
      `Pagination` (через проп из T-11), ссылке-дате в таблице и построчном «ред.»; `title="Сначала сохраните или отмените изменения"`.
- [ ] Режим проверки рендерит **снапшот** и не скрывается `isLoading` (список во время проверки не перезапрашивается);
      блок `isLoading` не должен подменять таблицу в фазах `review`/`saving`.
- [ ] `onSaved(updated)`: `refresh()` текущей страницы + `toast.success('Сохранено категорий: ' + updated)`.
- [ ] Справочник категорий поднимается на уровень страницы (`fetchCategories` один раз при `isAdmin`)
      и передаётся в таблицу и в хук — без второй загрузки.
- [ ] Тосты через существующий `useToast()`; новых зависимостей нет.
- [ ] Не-админ: кнопки нет, таблица и пагинация работают как раньше; `CommodityTable` без `reviewMode`.

`CommodityTable.tsx`:
- [ ] Новые пропсы: `reviewMode`, `drafts`, `onDraftChange`, `categories`, `onRetryRow` (для T-13), `controlsLocked`.
- [ ] В режиме проверки в колонке «Категория» — `<select value={drafts[id].value}>` с пустой опцией «—» первой;
      `Undefined` не выводится (как `selectableCategories` в `ReceiptDetails`).
- [ ] Под селектом — подпись источника `getSuggestionNote(source)` с классами `category-suggestion category-suggestion-<source>`;
      подпись выводится **у каждой** строки, включая повторяющиеся названия (C8).
- [ ] `title` строки/подписи = `drafts[id].error` при наличии.
- [ ] `touched`-строка показывает «изменено вручную».
- [ ] Вне режима проверки — текущее поведение без изменений (включая существующий построчный «ред.» и его известный дефект — **не исправлять**, вне объёма).
- [ ] Ключ строки — `commodityId` (не индекс).

`App.css`:
- [ ] При необходимости — стили для строки прогресса и inline-предупреждения; существующие `.category-suggestion*`
      переиспользуются. Замечено: классов `category-suggestion-existing` и `category-suggestion-precategorized`
      в `App.css` нет (есть только `cache`, `ai`, `undefined`) — при желании визуально различать их добавить
      нейтральные стили (косметика, не влияет на приёмку).

### Критерий готовности
Сквозной сценарий «Категоризировать → проверить → сохранить» работает на странице, контролы блокируются,
не-админ видит прежнюю страницу, тост содержит серверное `updated`.

### Проверка
```bash
cd Analytics/frontend && npm run build && npm run lint
```
Ручные сценарии T-15 (пункты 1–9, 13).

---

## T-13. Frontend: точечный повтор строки + предупреждение о несохранённом черновике

**Приоритет:** P1 · **Оценка:** 0.25 чел.-дн. · **Зависит от:** T-10, T-12

> Пробел плана: шаги 7–8 не описывают UI «повторить для этой строки», хотя C2 и ручная приёмка №10
> («точечный повтор») его требуют. Объём выводится из ADR-028 п. 1.3 (тот же эндпоинт с одним `commodityId`)
> и п. 2 — новых архитектурных решений не требуется.

### Затрагиваемые файлы
- `Analytics/frontend/src/hooks/useCommodityPageCategorization.ts` (`retryRow` — реализация в T-10).
- `Analytics/frontend/src/components/CommodityTable.tsx` (кнопка в колонке категории в режиме проверки).
- `Analytics/frontend/src/components/CommoditiesPage.tsx` (`beforeunload`).

### Чек-лист
- [ ] В режиме проверки у строк с `source === 'undefined'` (и у строк, не получивших результата) доступна
      кнопка «Повторить»; вызов — `retryRow(commodityId)`, без перезапуска всей страницы.
- [ ] Повтор не перезаписывает `touched`-строку с осмысленным значением (в том числе пришедший позже ответ).
- [ ] Ошибка повтора показывается в `title` строки, остальные строки и сохранение не блокируются.
- [ ] Во время `phase === 'saving'` кнопка недоступна; прогресс/`X из Y` при повторе не ломается
      (повтор не увеличивает `processed`, либо увеличивает по согласованному правилу — зафиксировать в комментарии).
- [ ] (Опционально, P2, риск «Потеря правок при reload вкладки») `beforeunload`-предупреждение, пока `phase === 'review'`.

### Критерий готовности
Ручной сценарий «ИИ не ответил по названию → строка «не определена» → «Повторить» → значение появилось»
работает без перезагрузки страницы и потери остальных правок.

### Проверка
```bash
cd Analytics/frontend && npm run build && npm run lint
```
Ручной сценарий T-15, пункт 10.

---

## T-14. Документация

**Приоритет:** P2 · **Оценка:** 0.1 чел.-дн. · **Зависит от:** T-02

### Затрагиваемые файлы
- `AGENTS.md` — таблица настроек Analytics, строка `CommodityBatchCategorization__SuggestTimeout`.

### Чек-лист
- [ ] В `AGENTS.md` добавлена строка: переменная, дефолт `00:00:25`, источник (`appsettings`
      `CommodityBatchCategorization:SuggestTimeout` / env), пояснение «бюджет одного suggest-запроса страницы;
      при истечении — частичный результат, остальное «не определена»; должен быть существенно меньше
      `proxy_read_timeout` nginx = 120 с», ссылка на ADR-028.
- [ ] Проверено, что нет дублей с уже существующим описанием `Precategorization__*`.
- [ ] Статус задачи `docs/tasks/commodities-page-categorization.md` **не менять** (решение PM).
- [ ] Новых ADR не создавать; amend-заметки в ADR-009/019/027 уже внесены вместе с ADR-028.

### Проверка
Ревью diff'а документации; сверка имён переменных с `docker-compose.yml` и `appsettings.json`.

---

## T-15. Финальная проверка и ручная приёмка

**Приоритет:** P0 · **Оценка:** 0.5–1.0 чел.-дн. · **Зависит от:** T-01…T-14

### Чек-лист авто-проверок
- [ ] `cd Analytics && dotnet test` — зелёный, включая `Architecture/ProjectDependencyTests`,
      `CommodityCategorizationServiceTests` и `PrecategorizedCommodityEndpointsTests` **без правок утверждений**.
- [ ] `cd Analytics && dotnet build` — без новых предупреждений.
- [ ] `cd Analytics/frontend && npm run lint` — `0 warnings`.
- [ ] `cd Analytics/frontend && npm run build` — успешно (`tsc -b && vite build`).
- [ ] `docker compose config` — переменная `CommodityBatchCategorization__SuggestTimeout` в окружении `analytics`.

### Чек-лист ручной приёмки (план 10.6, 13 пунктов)
- [ ] Админ, фильтр «Без категории», размер 5 → кнопка «Категоризировать страницу (5)».
- [ ] Запуск: селекты появляются сразу, строки заполняются порциями, прогресс «X из Y», страница не «висит».
- [ ] Фильтр «Все товары»: ранее категоризированная строка предзаполнена, «уже присвоена», в порции не участвует.
- [ ] Одинаковое название в 3 строках → одинаковое предложение и подпись у каждой; в логах Debug — один вызов ИИ на название.
- [ ] «Сохранить категории» во время обработки → сохраняется видимое, тост с числом **изменённых** строк.
- [ ] «Отмена» → черновик выброшен, блокировки сняты, данные не изменены.
- [ ] Пустой селект у ранее категоризированной строки + сохранение → категория **не сброшена** (C4).
- [ ] Фильтр «Без категории»/«Не указана» после сохранения: строки уходят, страница наполняется следующими;
      на последней странице — переход на предыдущую; «Найдено товаров» обновлено (C6 + T-09).
- [ ] `AI__BaseUrl=` (пусто) на dev: категоризация запускается, предупреждение об ИИ, часть строк из кэша/очереди,
      остальные «не определена», сохранение работает (C7). Проверить, что `503` **не** появился.
- [ ] ИИ медленный/таймаут по части названий → частичный результат, «не определена», «Повторить» для строки (T-13).
- [ ] Не-админ: кнопки нет; прямой `curl` на оба эндпоинта → `403`; `GET /api/commodities` не изменился.
- [ ] Конвейер из 2–3 страниц подряд без ручной пагинации.
- [ ] Режим проверки: пагинация, размер страницы, фильтр, «Обновить», дата-ссылка, «ред.» — заблокированы с подсказкой;
      смена страницы невозможна.
- [ ] Порядок строк после сохранения под фильтром «Все товары» не «прыгает» (ADR-028 п. 7).
- [ ] «Детали чека» (регрессия, см. T-04): suggest/apply работают как до изменений.

### Критерий готовности
Все авто-проверки зелёные, все пункты ручной приёмки отмечены; карта критериев приёмки (план, раздел 5)
закрыта.

---

## Открытые вопросы (эскалация к @software-architect)

> Исполнитель **не должен** решать эти пункты самостоятельно (см. «Замечания для реализации», п. 10 плана).

1. **Конфликт инварианта «тесты без изменений» и изменения конструктора `CommodityCategorizationService`.**
   ADR-028 требует, чтобы `CommodityCategorizationService` делегировал resolver'у, а план — чтобы
   `CommodityCategorizationServiceTests` остались без правок. Однако тесты создают сервис напрямую
   (`new CommodityCategorizationService(receiptRepo, commodityRepo, cache, precategorizedRepo, aiClient, aiOptions, logger)` —
   фабрика `CreateService`). Любое изменение набора зависимостей ломает **компиляцию** тестов.
   Варианты для решения архитектора:
   (a) разрешить механическую правку **только фабрики `CreateService`** (импорт resolver'а, без изменения утверждений) —
       предпочтительно, риск регрессии не растёт;
   (b) сохранить прежнюю сигнатуру конструктора (resolver внедряется фабрикой/опционально) — тесты не трогаются,
       но DI усложняется и появляется неиспользуемая зависимость.
   **Блокирует T-03.**

2. **UI точечного повтора (C2, ручная приёмка №10).** ADR-028 п. 1.3 фиксирует, что повтор использует тот же
   эндпоинт с одним `commodityId`, но в шагах 7–8 плана элемент интерфейса не описан. Подтвердить объём T-13:
   кнопка «Повторить» в колонке категории для строк с `source = 'undefined'`/не получивших ответа — этого достаточно?
   Требуется ли отдельный индикатор «повтор выполняется» на строке?

3. **Источник нормализованных имён для `TryAdd` в `ApplyAsync`.** ADR-028 фиксирует сигнатуру репозитория
   `Task<int> UpdateCategoriesAsync(userId, changes, ct)`, т.е. пары `id → name` не возвращает. Предлагается
   вызывать `GetByOwnerAsync(userId, changedIds)` **до** записи (2 чтения + 1 запись, без второго `SaveChanges`).
   Альтернатива — расширить контракт репозитория возвратом имён (изменение ADR-028, п. 9).
   Подтвердить выбор; **влияет на T-05**.

## Замечания исполнителям (не эскалации)

1. **Не менять** `POST /api/receipts/{id}/categories/suggest` и `PUT /api/receipts/{id}/categories` —
   ни `503` при пустом `AI:BaseUrl`, ни reset-семантику (C11, T-04).
2. Owner-фильтр писать **явно**: `c.Receipt.UserId == userId`; глобальный query-filter в `CommodityRepository`
   не активен (см. T-01).
3. `Results.Forbid()` не использовать — 500 вместо 403 (T-06, T-07).
4. Suggest ничего не кэширует; `TryAdd` — только после успешного сохранения (T-05).
5. В `precategorized_commodities` страница не пишет — только чтение (T-05).
6. Дедлайн и отмена клиента — **разные** токены (T-03, T-05); `FakeTimeProvider`, без реальных задержек в тестах.
7. `touched`-строки не перезаписывать пришедшими порциями (T-10).
8. Нормализацию названий на клиенте не дублировать (T-10).
9. Список во время режима проверки не перезапрашивать (T-12).
10. Известный дефект построчного редактора `CommodityTable` (`categoryId: null` в non-nullable `int` → `400`)
    **не исправлять** — вне объёма ADR-028; новый контракт на него не опирается.
11. Существующий дефект `Results.Forbid()` в `CommodityEndpoints.UpdateCategory` и `MerchantEndpoints` —
    в бэклоге, в этой задаче не трогается.
