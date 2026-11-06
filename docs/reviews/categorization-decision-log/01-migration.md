# Задача 01 - Миграция PostgreSQL: журнал `categorization_decisions` + исходное предложение очереди

- **Задача**: docs/plans/categorization-decision-log.md#t-01-миграция-postgresql-журнал-categorization_decisions--исходное-предложение-очереди
- **Дата**: 2026-11-06
- **Ревью**: раундов - 1, вердикт APPROVED (блокеров нет)

## Что сделано

Готова схема хранения журнала «предложено → принято»: в PostgreSQL появилась таблица
`categorization_decisions` с полями FR-1, двумя CHECK-констрейнтами и тремя индексами под
дедуп-выборку, порядок списка и фильтр «только расхождения». Дополнительно у очереди
предкатегоризации появилась неизменяемая колонка `original_proposed_category_id`: она
заполняется текущим `proposed_category_id` у существующих строк и дальше пишется только фоном,
поэтому правки администратора через `PUT .../category` перестают «затирать» исходное
предложение. C#-кода задача не содержит — домен, маппинг и сервисы идут в T-02 и далее.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261106120000_add_categorization_decisions.sql` - новый скрипт миграции: `CREATE TABLE categorization_decisions` (9 колонок по ADR-033 п. 1), `ck_categorization_decisions_suggested_not_undefined` (`suggested_category_id <> 0`), `ck_categorization_decisions_final_non_negative` (`final_category_id >= 0`), индексы `ix_categorization_decisions_combination` / `ix_categorization_decisions_created_at` / частичный `ix_categorization_decisions_mismatch`, затем `ADD COLUMN original_proposed_category_id` → backfill `= proposed_category_id` → `SET NOT NULL`; в шапке — обоснования решений (почему `suggestion_source` без CHECK, почему коды категорий без FK, зачем backfill, порядок развёртывания).
- `docs/plans/categorization-decision-log.md` - только таблица «Ход выполнения»: статус T-01 переведён в `approved`.
- `docs/reviews/categorization-decision-log/01-migration.md` - эта сводка.

Код C#, `MigrationRunner`, csproj и настройки не менялись: раннер сортирует `*.sql` по имени и
подхватывает новый скрипт автоматически, `Scripts\**\*` копируется в вывод сборки (проверено).

## Как проверено

- `cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet build` - Build succeeded, 0 warnings, 0 errors; файл появился в `bin/Debug/net10.0/Scripts/`.
- `cd Analytics && dotnet test` - Passed: 442, Failed: 0, Skipped: 0 (50 s). Предупреждения NU1902/NU1903 о пакетах существовали до задачи и к ней не относятся.
- Ручная проверка миграции настоящим `MigrationRunner` на одноразовом контейнере `postgres:18.0`:
  - прогнаны все 11 существующих скриптов, затем в `precategorized_commodities` вставлены 2 строки с `proposed_category_id` = 5 и 7, после чего прогнан полный каталог `Scripts` - новый скрипт применился, запись в `migration_scripts_history` появилась;
  - `\d categorization_decisions`: 9 колонок с нужными типами и NOT NULL, `created_at timestamptz NOT NULL DEFAULT now()`, 3 индекса (включая частичный `WHERE suggested_category_id <> final_category_id`), 2 CHECK, PK по `id`, FK отсутствует;
  - `\d precategorized_commodities`: `original_proposed_category_id integer NOT NULL`, у существующих строк значения 5 и 7, равные `proposed_category_id` (backfill выполнен);
  - негативные проверки: `suggested_category_id = 0` и `final_category_id = -1` отклоняются CHECK-констрейнтами; `accepted_by` без строки в `users` принимается (FK осознанно нет); `UPDATE proposed_category_id = 42` исходное значение (5) не меняет;
  - повторный прогон раннера: `Skipping already applied script 20261106120000_add_categorization_decisions.sql` - изменений нет (идемпотентность через `migration_scripts_history`).
- Ревьюер воспроизвёл миграцию на реальном Postgres независимо: схема, CHECK, индексы, backfill, идемпотентность и стиль совпали с ADR.

## Итоги ревью

- Исправлено по замечаниям ревьюера: blockers отсутствовали, правки не требовались.
- **Ограничение развёртывания (дублируется осознанно).** Миграцию на dev/prod применять **только** после готовности T-02 либо при временно выключенном фоне `Precategorization__Enabled=false`. Причина: после `SET NOT NULL` колонка обязательна, а код до T-02 (`PrecategorizedCommodityRepository.TryAddProposalAsync`) её ещё не пишет - фоновые вставки строк очереди начнут падать. `MigrationRunner` запускается отдельным шагом до старта API, и T-01 без T-02 ломает фоновые вставки. Ограничение зафиксировано и в шапке SQL-скрипта.
- **Гигиена коммита (предупреждение человеку).** В рабочем дереве есть посторонние незакоммиченные изменения: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и `appsettings.Development.json` (локальные значения - монго/AI/admin-id), `Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` (артефакт сборки фронтенда), staged ADR-009/027/028/033 и `docs/tasks/categorization-decision-log.md`, untracked `docs/plans/categorization-decision-log.md`. В коммит T-01 должен попасть **только** `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261106120000_add_categorization_decisions.sql` (плюс эта сводка и, при желании, сам файл плана - отдельным однозначным решением).
- **Дрейф SQL ↔ EF-модель (P2, вне рамок T-01).** Рекомендация ревьюера: в T-02 (или в финальной T-12) добавить Testcontainers-тест, который применяет `Scripts/*.sql` и сверяет полученную схему с EF-моделью. Сейчас такого паттерна в проекте нет: все Postgres-тесты создают схему из EF (`EnsureCreatedAsync`), поэтому расхождение миграции и маппинга ловится только ручной проверкой. Зафиксировано как пожелание, в T-01 не реализовывалось.
- **Мёртвый предикат `WHERE original_proposed_category_id IS NULL` в backfill - оставлен как есть.** Обоснование: скрипт выполняется один раз в транзакции раннера, предикат безвреден и делает `UPDATE` идемпотентным по данным; формулировка дословно совпадает с ADR-033 п. 1, а отклонение от ADR ради косметики не оправдано. Технически `WHERE` можно убрать - на результат это не влияет.

## Заметки для коммита

- Рекомендуемое сообщение коммита:
  `categorization decision log migration`
  (стиль последних коммитов проекта: короткое английское сообщение без префиксов; при желании - `t-01 of categorization decision log`).
- На что обратить внимание при ручной проверке:
  1. В коммит добавлять только скрипт миграции (и сводку), не захватывая локальные `appsettings*.json` и `wwwroot/index.html`.
  2. **Не применять миграцию на dev/prod до готовности T-02** либо выключить `Precategorization__Enabled` - иначе фоновые вставки в `precategorized_commodities` будут падать из-за NOT NULL.
  3. После применения на стенде проверить: `\d categorization_decisions` (3 индекса, 2 CHECK), `\d precategorized_commodities` (`original_proposed_category_id integer NOT NULL`), у старых строк значение = `proposed_category_id`, повторный `dotnet run` миграций - no-op.
