# Задача 04 — Repository: `HasBlockingQueueEntryAsync` + удаление мёртвой sibling-ветки из confirm

- **Задача**: docs/plans/commodity-name-normalization.md#t-04-repository-hasblockingqueueentryasync--удаление-мёртвой-sibling-ветки-из-confirm (редакция после правки 2026-11-09)
- **Дата**: 2026-11-09
- **Ревью**: раундов — 1, вердикт APPROVED (блокеров нет)

## Что сделано

Слой хранения получил C# pre-check кандидатов фона по очищенному ключу (ADR-034 решение C1):
новый метод `IPrecategorizedCommodityRepository.HasBlockingQueueEntryAsync(normalizedName,
merchantId, ct)` — «строка с тем же `normalized_name` у этого магазина в любом статусе **или**
любая `proposed`-строка с тем же ключом у любого магазина» (FR-3.4 ADR-027 по очищенному ключу).
Реализация — один индексируемый запрос (`AsNoTracking().AnyAsync` по
`p.NormalizedName == key && (p.MerchantId == merchantId || p.Status == Proposed)`, sargable по
`ix_precategorized_commodities_normalized_name`), guard пустого ключа как у
`GetKnownCategoryByNameAsync`, SQL-нормализации нет — ключ передаётся параметром. Из
`ConfirmAsync` **удалена** мёртвая ветка удаления «сестринских» `proposed` по сырому имени:
миграция T-02 сняла все `proposed`, а глобальный partial unique
`ux_precategorized_commodities_normalized_proposed` делает sibling-состояние недостижимым
(ADR-034 **D1** — «удаляется как мёртвая ветка», **D4** — «defense-in-depth» отклонено, тест с
искусственным сидом sibling-строк не вводился). Ветка по `normalized_name` не вводилась; на её
месте — только комментарий-проза со ссылкой D1/D4. Остальная семантика confirm без изменений:
`UPDATE commodities` по точному сырому имени и только строк без категории (NFR-1), `status =
applied` + `applied_at`, та же транзакция, те же статусы/коды `Applied`/`Conflict`/`NotFound`;
отклонение (`DELETE .../{id}`) — как раньше, ровно одна строка по `id`. XML-doc `ConfirmAsync`
переписан по трём шагам ADR-034 п. 4.5. Вызовов `HasBlockingQueueEntryAsync` из фона ещё нет —
это T-06.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/IPrecategorizedCommodityRepository.cs` —
  новый метод `HasBlockingQueueEntryAsync` с XML-doc по C1; док `ConfirmAsync` — три шага
  п. 4.5, упоминание sibling delete убрано, зафиксировано «сестринские `proposed` не ищутся и не
  удаляются» (D1).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs` —
  реализация `HasBlockingQueueEntryAsync` (один индексируемый запрос, guard пустого ключа);
  удаление sibling-ветки `ExecuteDeleteAsync` из `ConfirmAsync` (заменена комментарием-обоснованием,
  кода нет); `commodities`/транзакция/статусы/отклонение по `id` — без изменений.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityRepositoryTests.cs` —
  6 новых кейсов `HasBlockingQueueEntryAsync` (applied того же магазина блокирует; proposed того
  же магазина блокирует; proposed другого магазина блокирует; applied другого магазина НЕ
  блокирует — и блокирует своего; `false` при другом ключе и на пустой таблице;
  `matches_by_cleaned_key` — строка с ключом `туалетная бумага "сваё" (3сл, 4р)` находится
  запросом с ключом варианта `2195738 Туалетная бумага "СВАЁ" (3сл, 4р) 4810981001530`);
  новый `ConfirmAsync_updates_commodities_only_by_exact_raw_name` (регресс D1: вариант без кода
  не получает категорию при подтверждении строки с кодом, `UpdatedCommodities = 1`); правка
  `ConfirmAsync_updates_commodities_of_all_users_and_removes_sibling_proposals` →
  `..._touches_only_confirmed_row`: `Assert.Null(sibling)` → `Assert.NotNull` + статус `Proposed`,
  комментарий сида переписан со ссылкой ADR-034 D1/D4 («переключение на ключ — T-04» убрано),
  **сид не менялся**, остальные утверждения без изменений.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityWorkflowTests.cs` —
  аналогичная правка: `Confirm_updates_two_users_marks_applied_removes_siblings_and_fills_cache` →
  `..._marks_applied_and_touches_only_confirmed_row_and_fills_cache`; то же утверждение о sibling'е
  (остаётся `Proposed`) и комментарий; сид, наполнение кэша, журнал — без изменений.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizationBackgroundServiceTests.cs` —
  механическая правка: fake `FakeRepository` получает `HasBlockingQueueEntryAsync` (по умолчанию
  `false`), иначе тестовая сборка не компилируется; фон pre-check ещё не вызывает (T-06).
- `docs/plans/commodity-name-normalization.md` — только строки статусов T-03 (`approved` →
  `committed` — bookkeeping за уже сделанный человеком коммит `eac73e6`) и T-04 (`pending` →
  `in-progress` → `review` → `approved`) в таблице «Ход выполнения».

## Как проверено

Исполнитель:

- `cd Analytics && dotnet build` — 0 ошибок (30 warning'ов, все pre-existing, включая NU1903 Snappier).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorized"` — Passed: 65,
  Failed: 0 (37 с); до задачи — 58, добавлено 7 тестов.
- `cd Analytics && dotnet test` — Passed: 616, Failed: 0 (~61 с), включая Testcontainers-наборы и
  `Architecture/ProjectDependencyTests`; базлайн после T-03 — 609, +7 новых тестов.
- `git status` до/после — посторонние правки `appsettings.json`, `appsettings.Development.json`,
  `wwwroot/index.html` существовали до старта и не трогались.

Независимое ревью @code-reviewer (раунд 1, APPROVED):

- в `ConfirmAsync` от ветки остался только комментарий-проза со ссылкой D1/D4 (не заглушка кода);
  единственное удаление `proposed` в репозитории — `DeleteProposedAsync` по `id`;
- `HasBlockingQueueEntryAsync` точно по C1, sargable по `ix_..._normalized_name`, XML-doc
  соответствует;
- правка ровно двух confirm-тестов с корректным регрессом «sibling остаётся `Proposed`», сид не
  тронут, утверждения не ослаблены; `matches_by_cleaned_key` считает ключ через `NormalizeName`
  с обеих сторон;
- fake фона не маскирует T-06 (grep: вызовов `HasBlockingQueueEntryAsync` из фона нет);
- рамки задачи соблюдены (lookup/`GetCandidatesAsync`/фон не тронуты).
- Прогоны ревьюера: build 0 ошибок, фильтр 65/65, полный набор 616/616.

## Итоги ревью

- Исправлено по замечаниям ревьюера: блокеров не было; code-правок по неблокирующим suggestion'ам
  не требуется (обе — про гигиену коммита, учтены в «Заметках для коммита»).
- Допущения исполнителя (подтверждены ревьюером):
  1. В `matches_by_cleaned_key` использована категория `HouseholdChemicals` (в реестре категорий
     нет `Household`; туалетная бумага — бытовая химия) — на семантику теста не влияет.
  2. Кейсы «`false` при другом ключе» и «на пустой таблице» объединены в одном тесте
     `HasBlockingQueueEntryAsync_returns_false_for_other_key_and_empty_table` — план перечисляет
     их одним пунктом.
  3. NSubstitute-моки (`Substitute.For<IPrecategorizedCommodityRepository>` в 4 тестовых файлах)
     новый метод подхватывают автоматически — правок не потребовали, только hand-written fake.
  4. Комментарий на месте удалённой ветки в `ConfirmAsync` — не «замена ветки» (кода нет), а
     документирование причины удаления по D1/D4.
- Сознательно не делалось (по редакции плана): тест
  `ConfirmAsync_deletes_sibling_proposed_by_normalized_key` (невыполним in principle — вторая
  `proposed` отклоняется `23505`), любой сид в обход unique-индекса (запрещён D4), дублирование
  регресс-контроля unique-conflict из T-02
  (`TryAddProposalAsync_returns_false_when_proposed_with_same_normalized_key_exists_at_another_merchant`).

## Заметки для коммита

Рекомендуемое сообщение:

```text
T-04 queue pre-check + drop dead sibling branch

Add IPrecategorizedCommodityRepository.HasBlockingQueueEntryAsync per ADR-034 C1:
one indexed AnyAsync on normalized_name (same-merchant any status OR global
proposed), C# key passed as parameter, no SQL normalization. Remove the dead
sibling-proposed delete from ConfirmAsync (ADR-034 D1/D4): the T-02 migration
dropped all proposed rows and the global partial unique index makes the sibling
state unreachable; confirm now performs exactly the three steps of ADR-034 p.4.5.
Two confirm tests reworded to "touches only the confirmed row"; background fake
gets the new method (default false) until T-06 wires the pre-check.

Task T-04 (docs/plans/commodity-name-normalization.md).
```

Чек-лист для `git add`:

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/IPrecategorizedCommodityRepository.cs`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityRepositoryTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityWorkflowTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizationBackgroundServiceTests.cs`
- `docs/plans/commodity-name-normalization.md` — **только** строки статусов T-03/T-04 в таблице
  «Ход выполнения»; строка T-03 (`approved` → `committed`) — bookkeeping за уже сделанный
  человеком коммит `eac73e6`, а не самодеятельность исполнителя
- `docs/reviews/commodity-name-normalization/04-repository-precheck.md` (эта сводка)

**Не включать** в коммит: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`
(содержит личный Telegram-ID администратора и локальный AI-endpoint — категорически не в
коммит), `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` — все три существовали до
начала задачи и не трогались.

На что обратить внимание при ручной проверке:

- в `ConfirmAsync` нет ни одной операции удаления `proposed` (кроме отклонения строки по `id` в
  `DeleteProposedAsync`);
- подтверждение в `/precategorized` отвечает прежними телами/кодами (регресс контрактов — T-09);
- `HasBlockingQueueEntryAsync` — единственный новый запрос, читает по индексу
  `ix_precategorized_commodities_normalized_name`, нормализации в SQL нет.
