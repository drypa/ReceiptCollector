# Задача 05 — Resolver: очередь по очищенному ключу, ИИ по очищенному имени, переименование lookup

- **Задача**: docs/plans/commodity-name-normalization.md#t-05-resolver-очередь-по-очищенному-ключу-ии-по-очищенному-имени-переименование-lookup
- **Дата**: 2026-11-09
- **Ревью**: раундов — 1, вердикт APPROVED (блокеров нет)

## Что сделано

Общий конвейер категоризации (suggest чека и страницы «Товары») переведён на очищенное имя как
ключ шагов `precategorized` и `ai` (ADR-034 п. 4.1, FR-2/FR-3): очередь ищется по
`normalized_name`, в `IAiClient` уходит ключ группы (очищенное имя), дедупликация вызовов ИИ —
по нему же. Порядок `existing → merchant-default → cache → precategorized → ai`, wire-имена
источников, `name` в ответах (сырое), `503`/`aiAvailable`/`SuggestTimeout`/`MaxUniqueNames` и
прочие контракты не менялись (NFR-2). Lookup очереди переименован:
`GetCategoriesByExactNamesAsync` → `GetCategoriesByNormalizedNamesAsync` (интерфейс, реализация,
XML-doc: ключ — `normalized_name`, поиск в любом статусе, свежайшая строка
`applied_at ?? created_at`, невалидные коды отбрасываются); реализация фильтрует по
`p.NormalizedName` (sargable по `ix_precategorized_commodities_normalized_name`), `Distinct` по
ключам с `StringComparer.Ordinal`, ключ результата — очищенное имя; SQL-нормализации нет — ключ
считается в C# и передаётся параметром. В resolver'е очищенный ключ позиции вычисляется **ровно
один раз** на шаге 3 (уже считался для кэша) и переносится на шаги 4–5 через
`private readonly record struct PendingItem(Commodity Item, string Normalized)`: прежний
повторный пересчёт `NormalizeName` в раздаче ИИ-результатов удалён (горячая точка, NFR-3).
Шаг 4 — один batch-запрос по Distinct очищенным ключам (пустые фильтруются, как раньше), lookup
по ключу позиции; шаг 5 — группировка по ключу шага 3, `SuggestCategoryAsync(g.Key, ...)`,
сырое имя сохранено только в Warning-логах (FR-4). Деградация при ошибке чтения очереди
(Warning → ИИ) и проброс `OperationCanceledException` — без изменений.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/IPrecategorizedCommodityRepository.cs` —
  переименование метода lookup'а + XML-doc по ADR-034 п. 3/п. 4.1 (ключ `normalized_name`,
  любой статус, свежесть `applied_at ?? created_at`, невалидные enum отбрасываются, ключ из C#).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs` —
  предикат `normalizedList.Contains(p.NormalizedName)`, выборка и ключ результата —
  `NormalizedName`, `Distinct(StringComparer.Ordinal)` по ключам; свежесть, `Enum.IsDefined`,
  пропуск `Undefined` — без изменений.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs` —
  `PendingItem` (позиция + ключ с шага 3, нормализация один раз на позицию); шаг 4 — batch по
  очищенным ключам и lookup по ключу; шаг 5 — группировка по ключу, в ИИ уходит `g.Key`, сырое
  имя только в Warning-логах; `CreateResult` не менялся (name сырой); XML-doc класса обновлён.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategoryResolverTests.cs` —
  переименование вызовов мока; стаб деградации переведён на очищенное имя («пельмени»);
  3 новых теста: `Resolve_finds_queue_row_by_cleaned_key_and_skips_ai` (позиция
  `2195738 Туалетная бумага "СВАЁ" (3сл, 4р) 4810981001530` находит очередь по ключу без кодов →
  `precategorized`, ИИ не вызван, payload batch-запроса = очищенный ключ),
  `Resolve_sends_cleaned_name_to_ai` (`2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ` → ИИ получает
  `пирог с вишней по ре`, `name` в ответе сырой),
  `Resolve_deduplicates_variants_with_and_without_code_into_one_ai_call` (два варианта →
  `Received(1)`, обе позиции получают категорию из одного результата, `name` сырые).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategorizationServiceTests.cs` —
  переименование (6 мест); стабы ИИ с точными именами переведены в очищенную форму
  («пельмени», «бананы», «только ии», «йогурт», «сок»); ключи стабов очереди — очищенные;
  assertion payload'а batch-запроса очереди — очищенные ключи («хлеб»/«молоко», без «сыр»).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityBatchCategorizationServiceTests.cs` —
  переименование (4 места); `ArrangeAiSuccess`-стабы и Received — очищенные («молоко», «кофе»,
  «сломанный товар»); ключ стаба очереди — «чай»; XML-doc хелпера фиксирует контракт «ИИ получает
  очищенное имя».
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizationBackgroundServiceTests.cs` —
  fake-репозиторий: только переименование метода (поведение `NotSupportedException` сохранено;
  фон lookup не вызывает — это не T-06).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityRepositoryTests.cs` —
  тест `GetCategoriesByExactNamesAsync_returns_latest_row_and_skips_invalid_categories`
  переименован, запрос и утверждения — на очищенных ключах (сид не менялся: helper `Proposal`
  пишет `NormalizedName = NormalizeName(name)` с T-02); новый
  `GetCategoriesByNormalizedNamesAsync_matches_row_by_cleaned_key` (applied-строка
  `туалетная бумага "сваё" (3сл, 4р)` находится ключом варианта с EAN; регресс: сырое имя как
  параметр больше не матчится).
- `docs/plans/commodity-name-normalization.md` — только строка статуса T-05
  (`pending → in-progress → review → approved`) в таблице «Ход выполнения».

## Как проверено

Исполнитель:

- `cd Analytics && dotnet build` — Build succeeded, 0 ошибок (30 warning'ов, все pre-existing
  NU1902/NU1903).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryResolver|FullyQualifiedName~Precategorized"` —
  Passed: 86, Failed: 0 (38 с).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategorizationService|FullyQualifiedName~CommodityBatchCategorization"` —
  Passed: 103, Failed: 0.
- `cd Analytics && dotnet test` — Passed: 620, Failed: 0 (~59 с); базлайн после T-04 — 616,
  +4 новых теста (3 resolver + 1 repository).
- grep `GetCategoriesByExactNamesAsync` по `Analytics/` — 0 совпадений (старого имени не осталось).
- `git status` до/после — посторонние правки `appsettings.json`, `appsettings.Development.json`,
  `wwwroot/index.html` существовали до старта и не трогались; git-операций не выполнялось.

Независимое ревью @code-reviewer (раунд 1, APPROVED):

- `NormalizeName` вызывается ровно один раз на позицию (шаг 3, `PendingItem` переносит ключ на
  шаги 4–5; прежний повторный пересчёт в раздаче ИИ-результатов удалён);
- коллизия разных сырых имён с одним очищенным ключом в batch схлопывается `Distinct` и
  корректно мапится на обе позиции;
- свежесть (`AppliedAt ?? CreatedAt`), `Enum.IsDefined`, пропуск `Undefined` сохранены;
- в ИИ уходит `g.Key`; кэш в flows сохранения пишется по `NormalizeName(name)` — FR-3 согласован
  по всем ключам;
- `CreateResult` возвращает сырое имя; дедуп-тест не «театральный» — стаб привязан к точному
  аргументу ИИ;
- контракты, `503`-семантика, `aiAvailable`, `MaxUniqueNames` не тронуты;
- grep старого имени метода — 0 в коде;
- Прогоны ревьюера: build 0 ошибок, фильтр 86/86, полный набор 620/620.

## Итоги ревью

- Исправлено по замечаниям ревьюера: блокеров и code-правок по suggestion'ам не потребовалось.
- Допущения исполнителя (подтверждены ревьюером):
  1. «Механическая правка моков» включила адаптацию **данных** стабов: раз resolver шлёт очищенные
     ключи, стабы точных сырых имён (`SuggestCategoryAsync("Пельмени")`) и ключи словарей очереди
     (`["Молоко 3,2% 1л"]`) переведены в очищенную форму, иначе тесты падали бы. Поведенческие
     утверждения (источники, категории, коды, тексты ошибок) не менялись; изменены ровно два
     assertion'а payload'а (batch-запрос очереди; Received-аргумент ИИ) — это предмет FR-2/FR-3.
  2. Warning-логи `SuggestByAiAsync` сохраняют **сырое** имя позиции (`g.First().Item.Name`) —
     инвариант «сырое имя в логах» (FR-4); в ИИ уходит ключ.
  3. Пробельные наименования: шаг 4 фильтрует пустые ключи (как раньше); шаг 5 не фильтрует —
     для чека в ИИ вместо `"   "` теперь уходит `""`. **Принятое решение ревьюера: приемлемо,
     регресса нет**; guard (пропуск пробельных до ИИ) изменил бы контракты — вне объёма T-05.
     Пакетный сервис отсекает пробелы до resolver'а (`EmptyNameError`), тестов на этот край нет.
  4. Repository-тест `GetCategoriesByNormalizedNamesAsync_matches_row_by_cleaned_key` добавлен
     сверх списка плана как прямая EF-проверка предиката по `NormalizedName` (зеркало
     `HasBlockingQueueEntryAsync...matches_by_cleaned_key` из T-04, ADR-034 п. 6 «поиск по
     normalized_name в любом статусе»); не дублирует существующие тесты.
  5. Категория `HouseholdChemicals` для туалетной бумаги — прецедент T-04 (в реестре нет
     `Household`).
- Рамки T-05 соблюдены: фон (`PrecategorizationBackgroundService`), пакетный сервис,
  `ConfirmAsync`, миграции, API-контракты, `MaxUniqueNames`, кэш и настройки не тронуты.

## Заметки для коммита

Рекомендуемое сообщение:

```text
T-05 resolver: queue by normalized key, AI gets cleaned name

Rename IPrecategorizedCommodityRepository.GetCategoriesByExactNamesAsync to
GetCategoriesByNormalizedNamesAsync per ADR-034 p.3: the queue lookup filters on
normalized_name (indexed, C# key passed as parameter, no SQL normalization) and
keys its result by the cleaned name. The resolver now computes NormalizeName
exactly once per item (step 3, carried to steps 4-5 via PendingItem), sends the
group key (cleaned name) to IAiClient instead of the raw name, and looks up both
queue and AI results by that key - variants with and without leading/trailing
codes collapse into one AI call and one cache/queue key (FR-2/FR-3). Priorities
existing -> merchant-default -> cache -> precategorized -> ai, raw `name` in
responses, 503/aiAvailable/SuggestTimeout/MaxUniqueNames contracts unchanged.

Task T-05 (docs/plans/commodity-name-normalization.md).
```

Чек-лист для `git add`:

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/IPrecategorizedCommodityRepository.cs`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategoryResolverTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategorizationServiceTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityBatchCategorizationServiceTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizationBackgroundServiceTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityRepositoryTests.cs`
- `docs/plans/commodity-name-normalization.md` — строки T-04 (`approved` → `committed`,
  bookkeeping за уже сделанный человеком коммит `28b6142`; часть уже лежит в индексе) и T-05
  (`pending → approved`) в таблице «Ход выполнения»
- `docs/reviews/commodity-name-normalization/05-resolver-key.md` (эта сводка)

**Не включать** в коммит: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`
(содержит личный Telegram-ID администратора и локальный AI-endpoint — категорически не в
коммит), `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` — все три существовали до
начала задачи и не трогались.

На что обратить внимание при ручной проверке:

- suggest чека для позиции с кодом (`2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ`) → в логах/ответе
  ожидаемый источник; повторный suggest варианта без кода даёт `cache`/`precategorized`, ИИ не
  вызывается (ручная проверка из раздела «Тесты / ручная проверка» T-05, dev);
- в `PrecategorizedCommodityRepository.GetCategoriesByNormalizedNamesAsync` нет операций с
  `p.Name` — только `p.NormalizedName`; нормализации в SQL нет;
- ни один ответ suggest не изменился по форме (источники, `name`, коды) — меняется только
  качество источника/ключа.
