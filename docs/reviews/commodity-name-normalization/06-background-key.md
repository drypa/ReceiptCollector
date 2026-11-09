# Задача 06 — Фон предкатегоризации: pre-check очереди, ИИ и кэш по очищенному ключу

- **Задача**: docs/plans/commodity-name-normalization.md#t-06-фон-предкатегоризации-pre-check-очереди-ии-и-кэш-по-очищенному-ключу
- **Дата**: 2026-11-09
- **Ревью**: раундов — 1, вердикт APPROVED (блокеров нет)

## Что сделано

Фон предкатегоризации приведён к потоку ADR-034 п. 4.3 (FR-2/FR-3, решение C1): для каждого
кандидата очищенный ключ `normalized = CommodityNameNormalizer.NormalizeName(name)` вычисляется
**ровно один раз** (сразу после существующей проверки сырого имени) и используется для pre-check'а
очереди, lookup'а кэша и вызова ИИ. Новый шаг 3 — `HasBlockingQueueEntryAsync(normalized,
candidate.MerchantId)` (T-04): `true` — кандидат пропускается **до** existing/кэша/ИИ (вызов ИИ не
тратится, дубль предложения исключён ещё до вставки), `LogDebug` в существующем формате
`'{ProductName}' (merchant {MerchantId})`; ошибка pre-check'а попадает в существующий
per-candidate `catch` цикла сессии (Warning, пропуск — fail-safe, без дубля предложения, курсор
двигается). Глобальный existing-шаг (`GetKnownCategoryByNameAsync`) остался по **точному сырому**
имени (ADR-034, открытый вопрос 4 — зафиксировано комментарием). В `IAiClient.SuggestCategoryAsync`
теперь уходит `normalized` вместо сырого `name`; Warning/Info-логи вокруг ИИ сохраняют сырое имя
(FR-4). `TryAddProposalAsync` получает сырое имя + `MerchantId` как раньше — ключ `normalized_name`
пишет репозиторий (T-02), unique-конфликт по любому из индексов — прежний no-op; в фоне по вставке
ничего не менялось. Настройки `Precategorization:*`, рабочее окно (ADR-030), single-instance
допущение, SQL `GetCandidatesAsync` (безвредная избыточность точных NOT EXISTS по C1) — без
изменений.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/PrecategorizationBackgroundService.cs` —
  `ProcessCandidateAsync`: единственный расчёт `normalized` на кандидата (перенесён на шаг сразу
  после guard'а сырого имени); pre-check `HasBlockingQueueEntryAsync` до existing/кэша/ИИ с
  Debug-пропуском; кэш по `normalized` (как было); ИИ — `SuggestCategoryAsync(normalized, …)`;
  комментарий-фиксация existing по сырому имени (открытый вопрос 4); XML-doc `ProcessCandidateAsync`
  и `AddProposalAsync` обновлены (поток п. 4.3, fail-safe, no-op конфликт). Логи ИИ — сырое имя.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizationBackgroundServiceTests.cs` —
  fake-репозиторий: вместо константного `false` (заглушка T-04) настраиваемый
  `Blocking(normalizedName, merchantId)` + запись вызовов `PreChecks`; `FakeCache` — счётчик
  `TryGetCalls`; 4 новых теста: `Precheck_blocks_candidate_without_cache_ai_or_proposal_and_moves_cursor`
  (true → кэш/ИИ/TryAdd не вызываются — `TryGetCalls == 1` только за необработанный кандидат,
  `WarningCount == 0` (блокировка — Debug), pre-check вызван по очищенным ключам обоих кандидатов,
  курсор сдвинут, следующая сессия продолжается); `Precheck_error_skips_candidate_with_warning_and_session_continues`
  (исключение → Warning per-candidate catch, позиция пропущена, сессия продолжается, курсор
  двигается); `Ai_and_precheck_receive_cleaned_name_and_proposal_keeps_raw_name`
  (`2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ` → ИИ и pre-check получают `пирог с вишней по ре`,
  `TryAddProposalAsync` — сырое имя + merchantId); `Precheck_false_lets_all_candidates_through_unchanged_flow`
  (false → прежний поток; fake-аналог «applied другого магазина не блокирует»); регресс-правка
  стабов механическая: в `Undefined_and_ai_errors_do_not_create_rows_and_move_cursor` ключи стаба
  ИИ `Товар 1/2` → `товар 1/2` (ИИ получает очищенное имя), утверждения не менялись; остальные
  13 существующих тестов — без изменений.
- `docs/plans/commodity-name-normalization.md` — строки T-05 (`approved → committed`, bookkeeping
  за уже сделанный человеком коммит `61c8ab9`) и T-06 (`pending → in-progress → review → approved`)
  в таблице «Ход выполнения».

## Как проверено

Исполнитель:

- `cd Analytics && dotnet build` — Build succeeded, 0 ошибок (30 warning'ов, все pre-existing
  NU1902/NU1903) — как в базлайне T-05.
- `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizationBackgroundService"` —
  Passed: 18, Failed: 0 (14 существующих + 4 новых), ~0.8 с.
- `cd Analytics && dotnet test` — Passed: 624, Failed: 0 (~55 с); базлайн после T-05 — 620,
  +4 новых теста.
- `git status` до/после — посторонние правки `appsettings.json`, `appsettings.Development.json`,
  `wwwroot/index.html` существовали до старта и не трогались; git-операций не выполнялось.

Независимое ревью @code-reviewer (раунд 1, APPROVED):

- поток `ProcessCandidateAsync` шаг за шагом соответствует ADR-034 п. 4.3 и плану T-06
  (pre-check до existing/кэша/ИИ);
- fail-safe подтверждён кодом цикла сессии и тестом: курсор двигается и при блокировке, и при
  ошибке pre-check'а;
- логирование обосновано: блокировка — Debug (рутинный пропуск), ошибка — Warning существующего
  per-candidate catch;
- регресс чист: 13 существующих тестов не ослаблены, правка стабов механическая;
- тесты не «театральные»: `TryGetCalls == 1`, точные аргументы ИИ/pre-check/TryAdd захватываются;
- прогон ревьюера после финальной правки: фильтр 18/18, полный набор 624/624.

## Итоги ревью

- Исправлено по замечаниям ревьюера: блокеров не было; применена дешёвая suggestion — в
  `Precheck_blocks_candidate_...` добавлен `Assert.Equal(0, harness.Logger.WarningCount)`
  (фиксирует, что блокировка по C1 — Debug, не Warning); после правки прогоны повторены
  (фильтр 18/18, полный 624/624).
- Допущения исполнителя (подтверждены ревьюером):
  1. **Порядок pre-check относительно кэша.** В постановке задачи встречалась формулировка
     «обычно cache → pre-check → AI» с указанием следовать плану; план (шаги 1–7 T-06) и ADR-034
     п. 4.3/C1 задают **pre-check до existing/кэша/ИИ** (тест-лист плана: «pre-check true →
     ИИ/кэш/TryAdd не вызываются»). Ревьюер признал эту трактовку единственно верной: «кэш-хит без
     pre-check» в документах не требуется. Реализовано по плану/ADR.
  2. **Уровень лога блокировки — Debug** (план допускал Warning/Debug): рутинный пропуск, будет
     частым; Warning остался за ошибками (per-candidate catch). Зафиксировано тестом
     (`WarningCount == 0`).
  3. `Precheck_false_lets_all_candidates_through_unchanged_flow` — fake-уровень семантики «applied
     другого магазина не блокирует» (EF-семантика покрыта T-04); частично пересекается с
     `Ai_and_precheck_receive_cleaned_name...`, но соответствует отдельному пункту тест-листа плана.
- Рамки T-06 соблюдены: `GetCandidatesAsync` (raw SQL) и `GetKnownCategoryByNameAsync` не тронуты;
  репозиторий, миграции, API, настройки, пакетный suggest (T-07) не менялись.

## Заметки для коммита

Рекомендуемое сообщение:

```text
T-06 background precategorization: pre-check + cleaned key for cache/AI

Wire the background session to ADR-034 p.4.3: compute NormalizeName exactly once
per candidate and use it for the C1 queue pre-check (HasBlockingQueueEntryAsync
before existing/cache/AI - blocked candidates are skipped with a Debug log, no AI
call spent), the cache lookup and IAiClient.SuggestCategoryAsync (FR-2). The
global existing step stays on the exact raw name (open question 4); TryAddProposalAsync
still receives the raw name and the repository writes normalized_name (T-02),
unique conflicts remain a no-op. Pre-check errors fall into the existing
per-candidate catch (Warning, skip, cursor moves - fail-safe). Settings, work
window and GetCandidatesAsync SQL unchanged.

Task T-06 (docs/plans/commodity-name-normalization.md).
```

Чек-лист для `git add`:

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/PrecategorizationBackgroundService.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizationBackgroundServiceTests.cs`
- `docs/plans/commodity-name-normalization.md` — строки T-05 (`approved → committed`, bookkeeping
  за уже сделанный человеком коммит `61c8ab9`) и T-06 (`pending → approved`) в таблице
  «Ход выполнения»
- `docs/reviews/commodity-name-normalization/06-background-key.md` (эта сводка)

**Не включать** в коммит: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`
(содержит личный Telegram-ID администратора и локальный AI-endpoint — категорически не в
коммит), `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` — все три существовали до
начала задачи и не трогались.

На что обратить внимание при ручной проверке:

- после миграции и старта фона в очереди появляются строки с `normalized_name`, у варианта с кодом
  повторного предложения не создаётся (ручная проверка из раздела «Тесты / ручная проверка» T-06,
  dev);
- в `ProcessCandidateAsync` `NormalizeName` вызывается ровно один раз на кандидата; в ИИ уходит
  только очищенное имя, сырое имя — в логах и в строке очереди;
- `Precategorization:*` настройки не менялись; `GetCandidatesAsync` и `GetKnownCategoryByNameAsync`
  — без изменений.
