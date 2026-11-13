# Задача 08 - Amend-заметки ADR 009/022/027/028 и сквозная приёмка

- **Задача**: docs/plans/commodity-categories.md#t-08-amend-заметки-adr-009022027028-и-сквозная-ручная-приёмка (slug `08-adr-notes-acceptance`)
- **Дата**: 2026-11-13
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Последняя задача фичи. Четыре связанных ADR (009, 022, 027, 028) получили короткие
amend-заметки: в каждом — констатация того, что изменился только источник подсказок
(таблица `commodity_category_keywords`) и/или точка их применения, а прежние решения
остаются в силе. Ни одно прежнее решение не отменено, решения ADR не переписывались.

Второе: в ADR-035 п. 7 пример `GET /api/commodity-category-keywords` приведён к
фактической форме — элементы `boost`/`exclude` теперь объекты
`{ "id": "…", "word": "…" }` вместо массивов строк. Это устраняет внутреннее
противоречие ADR: решения G1/G3 утверждают «`id` уже есть в выдаче `GET` и
однозначен», а пример его не показывал, из-за чего UC-3 (удаление чипа) был бы
нереализуем. Под примером — пометка об amend (отклонение выявлено в T-07, эндпоинт
новый и на момент правки не выпущен, поэтому NFR-3 не затронут; порядок сортировки
слов не изменился). Разделы «Открытые вопросы» и «Допущения» ADR-035 не тронуты.

**Production-код в этой задаче не менялся**: правки только в `docs/` (5 ADR/план +
эта сводка). Код фичи полностью готов в задачах 01–07.

## Изменённые файлы

- `docs/adr/009-auto-commodity-categorization.md` - amend-заметка в разделе «Статус»
  после заметки ADR-034 (+6 строк).
- `docs/adr/022-commodity-categories-food-detailing.md` - amend-заметка в разделе
  «Статус» (+7 строк).
- `docs/adr/027-background-commodity-precategorization.md` - amend-заметка после
  заметки ADR-034 (+7 строк).
- `docs/adr/028-commodities-page-categorization.md` - amend-заметка после заметки
  ADR-034 (+6 строк).
- `docs/adr/035-commodity-category-keywords.md` - п. 7: пример `GET` к форме
  `{ id, word }` + пометка об amend под примером (+23/-2).
- `docs/plans/commodity-categories.md` - только статус задачи 08 в таблице «Ход
  выполнения» (`pending` → `in-progress` → `review` → `approved`).

Формат заголовка заметок — конвенция репозитория `> **Дополнено (YYYY-MM-DD):** …
См. [ADR 035](…)` (как заметки ADR-019/027/028/033/034 в этих же файлах), дата —
дата внесения заметки.

## Тексты amend-заметок

**ADR-009**

> **Дополнено (2026-11-13):** уточнения промта приходят из БД — таблицы
> `commodity_category_keywords`, где администратор ведёт boost-/exclude-слова для
> каждой категории; в `BuildPayload` рендерится только блок фактически совпавших
> подсказок, статический блок «Разграничения схожих категорий» удалён. Алгоритм
> `existing → … → ai`, контракты suggest/apply, `503` при пустом `AI:BaseUrl` и
> формат ответа не менялись. См. [ADR 035](035-commodity-category-keywords.md).

**ADR-022**

> **Дополнено (2026-11-13):** введённый этим ADR блок «Разграничения схожих
> категорий» (решение C2) перенесён в БД сидом таблицы
> `commodity_category_keywords` и удалён из кода (`BuildPayload`); содержимое
> перенесено в объёме, зафиксированном таблицей ADR-035 п. 2 (словоформы —
> единственное число, поясняющие ярлыки скобок словами не дублируются). Само
> решение не отменено: те же разграничения продолжают попадать в промт, изменился
> только их источник — вместо кода таблица, которую ведёт администратор. См.
> [ADR 035](035-commodity-category-keywords.md).

**ADR-027**

> **Дополнено (2026-11-13):** фоновая прекатегоризация передаёт в промт ИИ подсказки
> по ключевым словам из таблицы `commodity_category_keywords` — через единственный
> провайдер `ICommodityCategoryHintProvider` и по тому же очищенному имени, что и
> вызов ИИ (та же логика, что в ручном suggest). Приоритеты, рабочее окно, лимиты,
> single-instance допущение (D12) и стиль админских эндпоинтов — без изменений. См.
> [ADR 035](035-commodity-category-keywords.md).

**ADR-028**

> **Дополнено (2026-11-13):** пакетный suggest страницы «Товары» получает подсказки
> по ключевым словам из таблицы `commodity_category_keywords` автоматически — через
> общий resolver (`ICommodityCategoryHintProvider`, тот же, что в suggest чека и в
> фоне), без изменений контракта эндпоинта. `SuggestTimeout`, `aiAvailable` и
> assign-only семантика — без изменений. См.
> [ADR 035](035-commodity-category-keywords.md).

**Пометка в ADR-035 п. 7** (сразу под примером `GET`)

> **Дополнено (2026-11-13):** пример изначально показывал `boost`/`exclude`
> массивами строк. Отклонение выявлено в T-07: без `id` у слов UC-3 (удаление чипа)
> нереализуем — `DELETE` адресен только по `id`, и решения G1/G3 этого ADR прямо
> утверждают «`id` уже есть в выдаче `GET` и однозначен». Фактическая форма ответа —
> элементы списков объекты `{ "id": "…", "word": "…" }` (пример выше приведён к
> ней): это констатация фактической формы, а не новое решение. Эндпоинт новый,
> добавлен в T-04 этой же фичи и на момент правки не был выпущен, поэтому NFR-3
> (неизменяемость контрактов существующих эндпоинтов) не затронут; порядок
> сортировки слов (`word`, `OrdinalIgnoreCase` → `Ordinal`) не изменился.

Форма примера сверена с кодом (`CategoryKeywordItemDto(Guid Id, string Word)` →
camelCase `{ "id", "word" }`; `CommodityCategoryKeywordService.SortedWords` —
сортировка `OrdinalIgnoreCase` → `Ordinal` без изменений) и с wire-тестом
`CommodityCategoryKeywordEndpointsTests.GetAll_returns_every_category_without_undefined_including_empty_lists`.

## Сквозная приёмка сценариев 1–6

**Важная оговорка.** Статус `approved` по T-08 означает «документация приведена в
соответствие + поведение подтверждено автотестами», а **не** «ручная приёмка
пройдена». Живого AI, живого UI и фонового прохода на стенде в этой задаче не было:
сценарии из правой колонки **не выполнялись и не имитировались** — они остаются за
человеком.

| # | Сценарий | Подтверждено автотестами | Остаётся за человеком |
|---|---|---|---|
| 1 | Миграция применена, в БД есть слова сида; админ добавляет слово в UI | `CommodityCategoryKeywordSchemaTests.MigrationScript_creates_table_constraints_index_and_full_seed` (реальный PostgreSQL через Testcontainers: таблица, индекс, 3 CHECK, 167 строк сида — 94 boost / 73 exclude, точечные слова) + идемпотентность сида; на уровне API: `...EndpointsTests.Add_returns_created_element_with_trimmed_word_and_original_case`, `...ServiceTests.AddAsync_trims_word_keeps_case_and_stores_normalized_key` | Живой UI: `./up.dev.sh`, зайти админом в `/commodity-category-keywords`, добавить слово в boost категории, увидеть чип и счётчик `N / 200` |
| 2 | Suggest на 2–3 реальных товарах: в теле запроса блок подсказок ровно по совпавшим словам; при отсутствии совпадений блока нет | `OpenAiCompatibleAiClientTests.SuggestCategoryAsync_with_hints_prompt_contains_header_and_both_formulations`, `..._without_hints_prompt_is_product_catalog_and_format_only`, `..._prompt_no_longer_contains_hardcoded_disambiguation_block`; `CommodityCategoryResolverTests.Resolve_sends_hint_collected_by_normalized_name_to_ai`, `Resolve_sends_empty_hints_collection_when_nothing_matches` | 2–3 реальных товара из «Журнала категоризации» + живой AI: увидеть тело запроса (логи AI-клиента или логгирующий прокси на `AI:BaseUrl`) |
| 3 | Конфликт (слово в boost одной категории и exclude другой): в промте обе подсказки, exclude раньше | `CategoryKeywordMatcherTests.GetHints_puts_exclude_before_boost_for_the_same_category`, `GetHints_puts_exclude_first_even_when_its_category_code_is_greater`, `GetHints_returns_both_boost_hints_when_word_matches_two_categories`; `OpenAiCompatibleAiClientTests.SuggestCategoryAsync_hints_block_keeps_exclude_before_boost`, `..._truncation_drops_only_boost_and_keeps_every_exclude` | Финальное решение ИИ при конфликте — поведение модели, проверяется только живым прогоном |
| 4 | Удаление слова в UI → со следующего запроса подсказки нет, рестарт не требуется (NFR-4) | `...ServiceTests.RefreshAsync_called_after_committed_mutation_only`, `Failed_cache_refresh_does_not_change_mutation_outcome_and_cache_stays_dirty`; `InMemoryCommodityCategoryKeywordCacheTests.RefreshAsync_swaps_snapshot_and_previous_reference_stays_valid`, `GetSnapshot_first_call_loads_lazily_then_never_touches_repository`; `...EndpointsTests.Delete_returns_no_content_and_repeat_returns_not_found_with_exact_text` | Живой UI: снять чип, сразу выполнить suggest — подсказки нет без рестарта сервиса |
| 5 | Фоновая прекатегоризация получает те же подсказки | `PrecategorizationBackgroundServiceTests.Hints_are_computed_from_cleaned_name_and_passed_to_ai` (тот же провайдер, очищенное имя, подсказки доходят до `IAiClient`) | Проверка по Debug-логам живого сервиса (`HintCount` на вызов ИИ внутри рабочего окна) |
| 6 | `AI:BaseUrl` пуст → прежние `503` / `aiAvailable = false` | `ReceiptCategorizationEndpointsTests.Suggest_returns_503_when_ai_is_not_configured`; `CommodityBatchCategorizationEndpointsTests.Suggest_without_ai_configuration_returns_200_not_503` (`AiAvailable == false`); `OpenAiCompatibleAiClientTests.SuggestCategoryAsync_throws_invalid_operation_when_base_url_empty` | Ничего критичного — поведение полностью покрыто тестами |

## Итоговый чек-лист FR/NFR (по сводкам 01–07)

Функциональные требования:

| Требование | Чем подтверждено | Статус |
|---|---|---|
| FR-1. Хранение слов (категория + тип списка + слово, PostgreSQL) | T-01: миграция `20261112120000_add_commodity_category_keywords.sql`, EF-модель, `CommodityCategoryKeywordSchemaTests` (таблица, 3 CHECK, unique индекс) | выполнено, подтверждено автотестами |
| FR-2. Сид вместо хардкода | T-01: 167 строк сида (тест на Testcontainers); T-06: блок удалён из `BuildPayload`, `grep --include="*.cs" "Разграничения схожих категорий" Analytics/src` = 0 | выполнено, подтверждено автотестами |
| FR-3. API чтения (снимок по всем категориям, админ-только) | T-04: `GET /api/commodity-category-keywords`; T-07: форма `{ id, word }` + wire-assertions; тесты `...EndpointsTests.GetAll_*` (включая 401/403) | выполнено, подтверждено автотестами |
| FR-4. API изменения (добавление/удаление слова) | T-04: `POST` (200 + элемент) и `DELETE /{id}` (204/404); тесты `...EndpointsTests.Add_*`, `Delete_*` | выполнено, подтверждено автотестами |
| FR-5. Валидация (2–64 после trim, дубликат case-insensitive, лимит 200, регистр сохраняется) | T-02: `CommodityCategoryKeywordRulesTests`; T-04: `...ServiceTests.AddAsync_*`, `...EndpointsTests.Add_with_invalid_word_*`, `Add_duplicate_*`, `Add_limit_reached_*` | выполнено, подтверждено автотестами |
| FR-6. Матчинг по нормализованному имени (подстрока, нижний регистр) | T-02: `CategoryKeywordMatcherTests` (12 тестов: подстрока без границ, регистр, пустое/безбуквенное имя) | выполнено, подтверждено автотестами |
| FR-7. Сборка подсказок в промт (только совпавшие, boost/exclude формулировки, валидный JSON) | T-06: 14 тестов блока подсказок в `OpenAiCompatibleAiClientTests` (заголовок, обе формулировки, экранирование, лимит 50, `JsonDocument.Parse` тела) | выполнено, подтверждено автотестами |
| FR-8. Единая точка применения (чек, страница «Товары», фон) | T-05: один `ICommodityCategoryHintProvider`, обязательный параметр `hints` в `IAiClient` (забыть call site не даст компилятор); тесты resolver'а и фона | выполнено, подтверждено автотестами; живой прогон (сценарии 2 и 5) — за человеком |
| FR-9. UI: раздел «Категории товаров» | T-07: страница, сайдбар под `isAdmin`, чипы, добавление/удаление, ошибки валидации, состояния загрузки/пустых списков | выполнено, **требует ручной проверки на живом UI** (автотестов фронтенда в проекте нет — ADR-011/E1) |
| FR-10. Права (не-админ: нет пункта меню, 403 на страницу и API) | T-04: тесты `*_without_user_returns_unauthorized` / `*_with_non_admin_returns_forbidden`; T-07: гейт `useAdmin()` | выполнено на бэкенде (автотесты); UI-гейт — **требует ручной проверки** |

Нефункциональные требования:

| Требование | Чем подтверждено | Статус |
|---|---|---|
| NFR-1. Латентность (кэш в памяти, без похода в БД на каждый товар) | T-03: singleton-кэш неизменяемого снимка, `Volatile.Read` на горячем пути, hosted-прогрев; `InMemoryCommodityCategoryKeywordCacheTests.GetSnapshot_first_call_loads_lazily_then_never_touches_repository` | выполнено, подтверждено автотестами |
| NFR-2. Стоимость AI (в промт только совпавшие, объём не растёт с числом слов) | T-06: при пустых подсказках промт — 3 строки вместо 15; `..._without_hints_prompt_is_product_catalog_and_format_only`, тесты лимита 50 | выполнено, подтверждено автотестами |
| NFR-3. Обратная совместимость (контракты существующих suggest/categories не меняются) | T-04/T-05: регресс существующих тестов зелёный (782/782); новый ADR-035 п. 7 «Совместимость» | выполнено, подтверждено автотестами |
| NFR-4. Консистентность (изменение применяется к следующим запросам без рестарта) | T-03/T-04: `RefreshAsync` строго после commit с `CancellationToken.None`, «грязный» флаг + double-check; тесты кэша и сервиса | выполнено, подтверждено автотестами; живой сценарий 4 — за человеком |

## Открытые вопросы ADR-035 (остаются открытыми, реализация не выполнялась)

1. Словоформы сида: подстрока без стемминга (`варенье` не найдёт `варенья`) —
   морфологический поиск требует отдельного решения с оценкой ложных срабатываний.
2. Лимит подсказок в промте (`MaxPromptHints = 50`) — константа; делать ли настройкой,
   решать по факту эксплуатации.
3. Производительность матчинга при росте словаря — сейчас линейный проход; при
   кратном росте нужен предфильтр (`Aho-Corasick`), контракты провайдера не изменятся.
4. Ключевые слова для `Other`/служебных категорий — технически допустимы, UX-решение
   о скрытии в UI не принято.
5. Синхронизация кэша между инстансами — при втором экземпляре Analytics понадобится
   `LISTEN/NOTIFY` или короткий TTL (сейчас single instance, D12 ADR 027).
6. Телеметрия эффективности подсказок — сознательно отложена (потребует расширения
   `categorization_decisions`, чем задача запрещала).
7. Массовый импорт/экспорт словарей (CSV, копирование между средами) — вне области.

## Итоги ревью

Раунд 1 — APPROVED, блокеров нет. Ревьюер независимо подтвердил: 10 перечисленных
разработчиком тестов существуют под заявленными именами и проходят; формулировки
сверены с кодом (`OpenAiCompatibleAiClient.cs:341-344`,
`ReceiptCategorizationEndpoints.cs:48`, `PrecategorizationBackgroundService.cs:391,402`,
`CommodityBatchCategorizationService.cs:349`, `DependencyInjectionExtensions.cs:47`);
сид — ровно 167 строк (94 boost / 73 exclude), разложен по `(category_id, list_type)`
и сверен построчно с таблицей ADR-035 п. 2 без расхождений; wire-форма в примере п. 7
байт-в-байт соответствует `CategoryKeywordItemDto`, цитата G1/G3 дословная;
«Открытые вопросы»/«Допущения» не тронуты; границы соблюдены, новых настроек нет.
Прогоны ревьюера: `dotnet test` 782/782, build и lint зелёные.

Исправлено по замечаниям ревьюера (обе правки — только `docs/`, внесены до финального
прогона):

1. **ADR-022, формулировка объёма переноса.** Было: «содержимое сохранено полностью» —
   это сильнее факта: перенос по правилам ADR-035 п. 2 не переносит поясняющие ярлыки и
   уточнения исходного блока («свежие» фрукты, «свежее/замороженное» мясо, «в банках»,
   «посудомоечных машин», «личная гигиена», «канцтовары») и сводит словоформы к
   единственному числу. Стало: «содержимое перенесено в объёме, зафиксированном таблицей
   ADR-035 п. 2 (словоформы — единственное число, поясняющие ярлыки скобок словами не
   дублируются)».
2. **ADR-035, заголовок пометки.** Было «**Дополнение (2026-11-13, задача 07):**»,
   приведено к конвенции репозитория «**Дополнено (2026-11-13):**» (как во всех
   остальных ADR). Текст пометки не менялся.

Оставлено без изменений (с обоснованием):

- **Статус задачи 07 в диффе плана** (`approved` → `committed`): иначе таблица «Ход
  выполнения» была бы неправдивой после коммита человеком.
- **Различия формулировок в четырёх заметках**: сознательные — каждая констатирует
  своё изменение (009 — источник уточнений промта, 022 — перенос конкретного блока,
  027 — точка применения в фоне, 028 — автоматическое получение через общий resolver).
- **Отклонение «на момент правки не был выпущен»**: опирается на зафиксированное
  решение ревью T-07 (сводка 07, раунд 2), а не на собственную оценку.

Отдельно, вынесено ревьюером как вопрос к заказчику: в ADR-035 раздел «Связь с
другими ADR» по-прежнему говорит «требуется amend-заметка» про ADR 009/022/027/028,
хотя заметки теперь внесены. **Правка сознательно НЕ выполнена в T-08** — это выход
за рамки задачи (раздел описывает план работ на момент принятия ADR). Оформляется
отдельной задачей по решению заказчика.

## Отложенные follow-up'ы по фиче (сводки 03–07, кандидаты в отдельный аддитивный коммит)

Из задачи 03:

1. `InMemoryCommodityCategoryKeywordCache.cs` («грязный» путь `GetSnapshot`) — короткий
   cooldown ленивых перечитываний: при длительном простое БД каждое «грязное» чтение
   порождает серию попыток (каждая до таймаута Npgsql) и `LogWarning`.
2. `RefreshAsync` — отдельная ветка `catch` для `OperationCanceledException`, чтобы
   отмена не логировалась как «refresh failed» (семантика «не бросаем» не меняется).
3. `ReloadAsync` — двойная копия коллекции (`ToArray()` в кэше плюс копия в
   конструкторе снимка).

Из задачи 04:

4. Асимметрия `DeleteKeywordOutcome` в `DELETE`-эндпоинте: нет явной ветки `Deleted` +
   `throw` для неизвестного исхода (сейчас `_ => Results.NoContent()`).
5. Дубль предиката длины нормализованного слова в Api и Infrastructure — перенести в
   `CommodityCategoryKeywordRules` (Domain) при ближайшей правке Domain.
6. Опциональный короткий бюджет таймаута на post-commit инвалидацию кэша (зависшая БД
   не должна удерживать запрос админа).

Из задачи 05:

7. `PrecategorizationBackgroundService.cs:392-395` — Debug-лог пишет `normalized`,
   тогда как остальные логи метода используют сырое `name` (ADR-034 FR-4).
8. `CommodityCategoryResolver.cs:352` — `GetHints` вызывается после взятия семафора;
   вынести до `WaitAsync`, чтобы семафор удерживался ровно на время HTTP-вызова.

Из задачи 06 (решения ревьюера «оставлено как есть», не дефекты):

9. Фикстура `CategoryCatalog` в тесте «старых строк нет» намеренно не совпадает с
   полным реестром.
10. «Избыточное» утверждение в тесте лимита подсказок — оставлено как дополнительная
    защита формата.

Из задачи 07:

11. `useCommodityCategoryKeywords.ts:143,167` — мутации без `AbortSignal`: после
    unmount завершающийся `await load()` может дёрнуть `setState` (в React 18
    безвредно); при желании — общий controller на жизненный цикл вью.
12. `CommodityCategoryKeywordsPage.tsx:78-81` — кнопка «Обновить» сверх минимума
    требований; оставлена как стиль существующих админских страниц.

## Как проверено

- `cd Analytics && dotnet test` - Passed: 782, Failed: 0, Skipped: 0 (1 m 01 s). Код
  не менялся, результат ожидаемый.
- Целевые прогоны (подтверждение сценариев 1–6):
  - `--filter "…CommodityCategoryKeywordSchema|…CategoryKeywordMatcher|…OpenAiCompatibleAiClient"` - Passed: 52, Failed: 0;
  - `--filter "…InMemoryCommodityCategoryKeywordCache|…CommodityCategoryKeywordService|…Precategorization|…CommodityCategoryResolver"` - Passed: 98, Failed: 0;
  - `--filter "…ReceiptCategorizationEndpoints|…CommodityBatchCategorizationEndpoints|…CommodityCategoryKeywordEndpoints"` - Passed: 93, Failed: 0.
- `cd Analytics/frontend && npm run build` - зелёный (71 модуль, 232 мс);
  `npm run lint` - зелёный (`--max-warnings 0`).
- `git checkout -- Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` -
  артефакт сборки откачен; `git status --porcelain` показывает только 6 файлов `docs/`.
- Контроль границ: `git diff --stat 0f80e44~1..HEAD` - 60 files changed,
  6605 insertions(+), 144 deletions(-), все файлы в `Analytics/` и `docs/`;
  `git diff --name-only 0f80e44~1..HEAD | grep -E "^backend/|^bot/|docker-compose|^\.env\.example|^AGENTS\.md|^docker/nginx|categorization_decisions"` -
  пусто; `appsettings*` в диффе ветки нет;
  `grep -cE "^\+.*(class [A-Za-z]+Options|IOptions<|GetSection\()"` по `Analytics/src` - 0
  (новых настроек нет).
- Ревьюер независимо подтвердил: `dotnet test` 782/782, build и lint зелёные,
  10 поименованных тестов существуют и проходят, сид 167 (94/73) сверен построчно с
  таблицей ADR-035 п. 2, wire-форма примера п. 7 совпадает с `CategoryKeywordItemDto`.

## Заметки для коммита

Рекомендуемое сообщение коммита:

```text
docs(adr): amend-заметки 009/022/027/028 и приведение ADR-035 п. 7 к фактической форме GET (T-08)
```

Файлы задачи (только `docs/`, production-код не менялся):

- `docs/adr/009-auto-commodity-categorization.md`
- `docs/adr/022-commodity-categories-food-detailing.md`
- `docs/adr/027-background-commodity-precategorization.md`
- `docs/adr/028-commodities-page-categorization.md`
- `docs/adr/035-commodity-category-keywords.md`
- `docs/plans/commodity-categories.md` (только статус задачи 08)
- `docs/reviews/commodity-categories/08-adr-notes-acceptance.md` (эта сводка)

В коммит **НЕ включать**: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json` (локальные
настройки окружения: AI-эндпоинт, Mongo-креды, личные Telegram id) и
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` (артефакт
`npm run build`, в рабочем дереве уже откачен). `git add` — по явным путям, не `-A`.

**После коммита обязательна ручная приёмка сценариев 1–6 на живом окружении**
(см. таблицу «Сквозная приёмка», правая колонка): живой AI, живой UI и фоновый проход
на стенде в этой задаче не выполнялись. Это последнее, что осталось по фиче
«Категории товаров»: после приёмки задача 08 получает статус `committed`, а
открытые вопросы ADR-035 и отложенные follow-up'ы оформляются отдельными задачами по
решению заказчика.
