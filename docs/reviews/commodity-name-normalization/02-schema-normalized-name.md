# Задача 02 — Схема: SQL-миграция, EF-модель, запись `normalized_name` при вставке

- **Задача**: docs/plans/commodity-name-normalization.md#t-02-схема-sql-миграция-ef-модель-запись-normalized_name-при-вставке
- **Дата**: 2026-11-09
- **Ревью**: раундов — 1, вердикт APPROVED (блокеров нет)

## Что сделано

Введён индексируемый очищенный ключ строки очереди предкатегоризации (ADR-034 п. 2, решения B1
и E1): колонка `precategorized_commodities.normalized_name varchar(256) NOT NULL`. Миграция
`20261109120000_add_precategorized_normalized_name.sql` повторяет шаги 1–9 ADR дословно:
`ADD COLUMN` → удаление `proposed` (FR-5, пересоберутся фоном) → одноразовая SQL-копия правила в
`pg_temp` (после сеанса миграции исчезает, в runtime функции нет) → preflight `RAISE EXCEPTION` при
`length(...) > 256` для `applied` → backfill только `applied` → `SET NOT NULL` → индекс
`ix_precategorized_commodities_normalized_name` → частичный уникальный
`ux_precategorized_commodities_normalized_proposed (normalized_name) WHERE status = 0` (один
`proposed` на очищенный ключ глобально, FR-3.4 ADR-027) → `DROP FUNCTION`. EF-модель описывает ту
же схему (колонка + оба индекса с точными SQL-именами, у уникального `HasFilter("status = 0")`).
`TryAddProposalAsync` — единственное место записи ключа: `NormalizeName` вызывается ровно один раз
на строку, guard длины сырого и очищенного имени (≤ 256, иначе `LogWarning` с длинами и `false`),
`catch` unique-violation покрывает оба индекса. Чтение/поиск по ключу, sibling delete и API — не
трогались (это T-04/T-05); поведение эндпоинтов и фона не изменилось.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261109120000_add_precategorized_normalized_name.sql` —
  новый скрипт, шаги 1–9 ADR-034 п. 2 дословно; заголовок объясняет `pg_temp` (одноразовость,
  решение A2), `DELETE proposed` / сохранение `applied` (FR-5, E1), глобальность уникальности
  `proposed` (FR-3.4 ADR-027 по очищенному ключу), идемпотентность через `MigrationRunner`,
  порядок деплоя и откат.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityEntity.cs` —
  свойство `NormalizedName` (string) с XML-doc.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/PrecategorizedCommodityConfiguration.cs` —
  `.IsRequired().HasMaxLength(256)`; `ix_precategorized_commodities_normalized_name` и частичный
  уникальный `ux_precategorized_commodities_normalized_proposed` (`HasFilter("status = 0")`);
  комментарий «индексы объявлены и в EF-модели, и в SQL: тесты создают схему через
  `EnsureCreatedAsync`».
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs` —
  `TryAddProposalAsync`: единственный вызов `NormalizeName` на строку, guard сырого (как был) и
  очищенного имени, `entity.NormalizedName = normalized`, комментарий в `catch` (оба индекса).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityRepositoryTests.cs` —
  helper `Proposal(...)` заполняет `NormalizedName` (+необязательный параметр `normalizedName`);
  raw-SQL `INSERT` дополнен колонкой `normalized_name`; новые тесты: запись
  `normalized_name == NormalizeName(name)` для имени с ведущим EAN; unique-conflict по новому
  частичному индексу (второй `proposed` с тем же ключом у другого магазина → `false`, строка не
  вставлена); факт ICU-рантайма на 200×`İ`; граница 256 включительно. Утверждения существующих
  тестов не менялись.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityWorkflowTests.cs` —
  тот же helper + правка сида Confirm (см. «Итоги ревью», п. 2).
- `docs/plans/commodity-name-normalization.md` — только строка статуса T-02 в таблице
  «Ход выполнения» (`pending` → `in-progress` → `review` → `approved`).

Посторонние локальные правки — `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`appsettings.Development.json`, `wwwroot/index.html`, а также незакоммиченная правка
`docs/adr/034-commodity-name-normalization.md` («открытый вопрос 8») — существовали до начала
задачи, не трогались и в коммит не входят.

## Как проверено

- `cd Analytics && dotnet build` — 0 ошибок (warning'и pre-existing, включая NU1903 Snappier).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorized"` — Passed: 57,
  Failed: 0 (после добавления теста unique-conflict по новому индексу).
- `cd Analytics && dotnet test` — Passed: 608, Failed: 0 (~55 с), включая Testcontainers-наборы и
  `Architecture/ProjectDependencyTests`; baseline после T-01 — 604, добавлено 4 теста.
- Смоук миграции на временном `postgres:16-alpine` (исполнитель, контейнер удалён): скрипт
  выполнен целиком (`DELETE 2` proposed, `UPDATE 3` applied); backfill:
  `2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ → пирог с вишней по ре`, `*3455614 → *3455614` (fallback),
  `Молоко → молоко`; `SET NOT NULL` прошёл; оба индекса созданы; в новом сеансе: `INSERT` без
  ключа → `23502`, второй `proposed` с тем же ключом → `23505`
  (`ux_precategorized_commodities_normalized_proposed`), `proposed` с ключом существующего
  `applied` разрешён (по замыслу ADR), `pg_temp`-функция недоступна.
- Независимое ревью @code-reviewer (раунд 1): SQL скрипта посимвольно совпадает с ADR п. 2
  (токен-дифф); все 13 скриптов применены к чистому `postgres:16-alpine` —
  `normalized_name varchar(256) NOT NULL`, оба индекса с точными именами, частичный
  `WHERE status = 0`; EF↔SQL паритет; `TryAddProposalAsync` — единственный вызов нормализации,
  guard'ы, `catch` покрывает оба индекса; повторен смоук (proposed удалены, backfill совпадает с
  C#-правилом, `23502`/`23505`, `pg_temp` недоступна). Прогоны ревьюера: build 0 ошибок,
  фильтр 56 passed, полный набор 607/607 (до добавления теста unique-conflict).

## Итоги ревью

- Принятые отклонения исполнителя (обоснования подтверждены ревьюером):
  1. Тест плана «сырое ≤ 256, очищенное > 256 (200×`İ`) → `false`» невыполним в этом рантайме:
     пример опирается на полные регистропреобразования .NET Framework; в .NET 10 (ICU)
     `ToLowerInvariant` использует простые соответствия и строку не расширяет (ревьюер
     независимо проверил сканом всех кодовых точек — расширяющих символов нет). Guard оставлен
     по плану/ADR как defense-in-depth (риск ADR-034 «Переполнение varchar(256) после lowercase —
     Низкая»; та же логика, что в оговорке T-03 про preflight). Замена: тесты, фиксирующие
     ICU-факт (200×`İ` вставляется, ключ 200 символов) и границу 256 включительно.
  2. Правка двух Confirm-сидов допустима: новый частичный уникальный индекс запрещает состояние
     «две `proposed` с одним очищенным ключом» (в сидах были две `proposed` «Молоко» у разных
     магазинов), «сестринской» строке задан явный иной ключ (`normalizedName: "sibling"`) с
     комментарием; утверждения тестов не менялись.
- Исправлено по неблокирующей suggestion ревьюера (после APPROVED): добавлен тест
  `TryAddProposalAsync_returns_false_when_proposed_with_same_normalized_key_exists_at_another_merchant`
  — закрывает ранее не покрытый путь `catch` по новому частичному индексу (прежний индекс
  `(name, merchant_id)` такую пару не запрещает); прогоны после добавления — фильтр 57, полный
  набор 608. `candidate.Name?.Length ?? 0` в прежнем guard'е не тронут (вне рамок задачи).

## Эскалация до старта T-04

Факт, выявленный ревьюером (решение — за @software-architect и project-manager, здесь не
принимается): частичный уникальный индекс `(normalized_name) WHERE status = 0` в сочетании с
шагом удаления `proposed` делают состояние «две `proposed` с одним очищенным ключом»
принципиально недостижимым в схеме после этой миграции. Следствия:

- заявленный в плане T-04 тест `ConfirmAsync_deletes_sibling_proposed_by_normalized_key`
  (посев двух `proposed` с одним ключом) невыполним in principle;
- сам sibling delete по ключу — доказуемый no-op: на момент confirm подтверждаемая строка —
  единственная `proposed` своего ключа; текущий delete по сырому имени после T-02 тоже мёртв;
- ADR-034 D1 внутренне противоречив: мотивировка sibling delete («не оставались дубли одного
  товара») обнуляется шагом 8 миграции.

Рекомендация ревьюера: уточнить ADR-034 D1; убрать невыполнимый тест из T-04 и заменить его
регресс-тестом `TryAddProposalAsync` «второй `proposed` с тем же ключом у другого магазина →
`false`, строка не вставлена» (такой тест уже добавлен в этой задаче — см. «Итоги ревью»).
Правка плана/ADR — задача project-manager и архитектора до старта T-04.

## Заметки для коммита

Рекомендуемое сообщение:

```text
feat(analytics): normalized_name key for precategorization queue

Add precategorized_commodities.normalized_name per ADR-034 p.2 (decision B1):
migration 20261109120000 drops proposed rows (FR-5), backfills applied via a
one-shot pg_temp copy of the cleaning rule, sets NOT NULL and creates the
plain plus partial-unique (status = 0) indexes; TryAddProposalAsync writes the
key once per row with raw/normalized length guards.

Task T-02 (docs/plans/commodity-name-normalization.md).
```

Чек-лист для `git add`:

- `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261109120000_add_precategorized_normalized_name.sql`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityEntity.cs`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/PrecategorizedCommodityConfiguration.cs`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityRepositoryTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedCommodityWorkflowTests.cs`
- `docs/plans/commodity-name-normalization.md` (только строка статуса T-02)
- `docs/reviews/commodity-name-normalization/02-schema-normalized-name.md` (эта сводка)

**Не включать** в коммит: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` (посторонние локальные правки)
и `docs/adr/034-commodity-name-normalization.md` (незакоммиченная правка «открытый вопрос 8» —
отдельный документный коммит, решает человек).

**Шаг эксплуатации (не код этого коммита):** применение миграции к dev/prod — отдельная процедура
с короткой остановкой Analytics: дамп таблицы (`pg_dump -t precategorized_commodities` или
`./backup.sh`) → остановить `analytics` → `docker compose run --rm analytics-migrations` →
запустить новый образ `analytics`. Откат после успешной миграции простым запуском прежнего образа
невозможен (NOT NULL-колонка). Ручная проверка после применения (dev):
`\d precategorized_commodities` показывает `normalized_name NOT NULL`, оба новых индекса и
прежние `ux_..._name_merchant`, `ix_..._status`.
