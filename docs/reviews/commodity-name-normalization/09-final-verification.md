# Задача 09 — Финальная проверка, релизная инструкция, ручная приёмка

- **Задача**: docs/plans/commodity-name-normalization.md#t-09-финальная-проверка-релизная-инструкция-ручная-приёмка
- **Дата**: 2026-11-10
- **Ревью**: раундов — 1, вердикт APPROVED (блокеров нет)

## Что сделано

Финальная проверочная задача фичи «усиление нормализации названий товаров»: подтверждён
сквозной результат T-01…T-08 и отсутствие регрессий (NFR-1…NFR-4). **Продуктовый код не
изменялся** — задача свелась к: (1) полному прогону `dotnet test` (635/635, включая
`Architecture/ProjectDependencyTests` и паритетный тест миграции); (2) смоуку **всех**
контрольных запросов раздела «Ответ на открытый вопрос №6 ADR» на временном
`postgres:16-alpine` по прецеденту T-02/T-03: дорелизная схема (первые 12 SQL-скриптов,
каждый в своей транзакции, как `MigrationRunner`) → посев 7 `applied` (примеры задачи с
кодами, вариант без кода, «только код», collapse пробелов, второй магазин) + 2 `proposed`
→ применение **реального файла** `20261109120000_add_precategorized_normalized_name.sql` —
все шесть запросов дали ожидаемые результаты; (3) созданию постоянного runbook
`docs/runbooks/commodity-name-normalization-deploy.md` — порядок применения миграции
(дамп → остановка Analytics → миграции → новый образ) с обоснованием NOT NULL-окна,
контрольными запросами, откатом (после `SET NOT NULL` простой откат образа невозможен:
forward-fix или дамп + удаление колонки/индексов + удаление строки из
`migration_scripts_history`) и частыми проблемами; (4) сверке границ (нет правок вне
Analytics). Дефектов не найдено, эскалаций нет. Ручная приёмка на dev-стенде осталась за
человеком — чек-лист ниже.

## Изменённые файлы

- `docs/runbooks/commodity-name-normalization-deploy.md` — новый постоянный runbook
  эксплуатации (шаги 0–4 с ✅/⛔ по прецеденту `mongodb-upgrade-manual-ops.md`; контрольные
  запросы 1–6 с ожидаемыми результатами и оговоркой про fallback «только цифры ≥6» для
  запроса 4; откат до/после миграции; финальный чек-лист; достоверная проверка применения —
  `migration_scripts_history`, а не `Exited (0)` после `run --rm`).
- `docs/plans/commodity-name-normalization.md` — строка статуса T-09 в таблице
  «Ход выполнения» (`pending → in-progress → review → approved`); в рабочем дереве также
  предсуществующая bookkeeping-правка T-08 (`approved → committed` за уже сделанный
  человеком коммит `6c2021e`).
- `docs/reviews/commodity-name-normalization/09-final-verification.md` — эта сводка.

Правки продуктового кода и тестов: **0 файлов** (`git diff HEAD --name-only --
'Analytics/**/*.cs'` — пусто).

## Как проверено

### Прогоны (фактические результаты)

| Команда | Результат разработчика | Подтверждение ревьюера |
|---|---|---|
| `cd Analytics && dotnet test` | **Passed: 635, Failed: 0, Skipped: 0** (54 с) — базлайн T-08 (635) | независимо **635 / 635** |
| `dotnet test --filter "FullyQualifiedName~ProjectDependencyTests"` | **4 / 0** (77 мс) | **4 / 4** |
| `dotnet test --filter "FullyQualifiedName~PrecategorizedNormalizedNameMigration"` | **1 / 0** (362 мс) | **1 / 1** |

Флейк Testcontainers `mongo:7.0` (`Address already in use`) в этих прогонах не проявился;
при проявлении — удалить мёртвые контейнеры (`docker ps -a` → `docker rm -f` **без** `-v`)
и повторить (известный флейк среды, не регрессия).

### Смоук контрольных запросов (временный `postgres:16-alpine`, удалён без `-v`)

Процедура исполнителя: первые 12 скриптов `Migrations/Scripts/` поимённо (каждый
`psql --single-transaction` — скрипты содержат `BEGIN` без `COMMIT`, транзакцию ведёт
раннер) → посев: 7 `applied` + 2 `proposed` (состояние «до»: `status=0 → 2`,
`status=1 → 7`) → реальный файл миграции одним сеансом `--single-transaction`
(`DELETE 2`, `UPDATE 7`, `SET NOT NULL`, оба индекса, `DROP FUNCTION`).

| № | Запрос (план, «Ответ на открытый вопрос №6») | Ожидание | Факт (исполнитель) |
|---|---|---|---|
| 1 | пустые/NULL ключи | 0 | **0** |
| 2 | `status = 0` (до старта фона) | 0 | **0** |
| 3 | разбивка по статусам | `status=0` нет; `status=1` ≥ дорелизного | **только `1 | 7`** (= дорелизное) |
| 4 | ключи с ведущими/хвостовыми кодами | 0 | **0** |
| 5 | spot-check трёх имён задачи | `пирог с вишней по ре` / `туалетная бумага "сваё" (3сл, 4р)` / `рж.край хлеб нар.300г` | **ровно так** |
| 6 | `\d precategorized_commodities` | `normalized_name varchar(256) not null` + оба новых индекса | **`character varying(256)` not null; `ix_precategorized_commodities_normalized_name`; `ux_precategorized_commodities_normalized_proposed UNIQUE WHERE status = 0`; прежние `ux_..._name_merchant`, `ix_..._status` на месте** |

Дополнительные проверки (прецедент смоука T-02): полный backfill 7 строк совпал с правилом
C# (fallback `*3455614` → `*3455614`; `Молоко  2.5%` → `молоко 2.5%`; вариант без кода дал
тот же ключ `пирог с вишней по ре`); `INSERT` без ключа → `23502`; `proposed` с ключом
существующего `applied` **разрешён** (partial unique только на `status = 0` — по замыслу
ADR); второй `proposed` с одним ключом у другого магазина → `23505` по
`ux_..._normalized_proposed`; `pg_temp`-функция в новом сеансе недоступна (`pg_proc` — 0).

Ревьюер **независимо повторил смоук** (12 скриптов → посев 7+2 → реальный файл → все
контрольные запросы совпали, spot-check точный, `23502`/`23505`, `pg_temp` мертва) и
сверил runbook с `docker-compose.yml` / `up.sh` / `backup.sh` / `.env.example` (сервисы
`analytics`/`analytics-migrations`, контейнеры `receipt-analytics`/`receipt-postgres`,
БД `receipts`, образ `drypa/receipt-collector-analytics` — всё верно). Шаг удаления строки
из `migration_scripts_history` в откате признан корректным и полезным (сверено с
`MigrationRunner.cs:72-76, 166-172`).

### Границы вне Analytics

`git status --short` / `git diff HEAD`: правок в `backend/`, `bot/`, `docker-compose*.yml`,
`.env*`, `AGENTS.md`, `Analytics/frontend/` **нет**; в `Analytics/` — только
предсуществующие локальные артефакты (`appsettings.json`, `appsettings.Development.json`,
`wwwroot/index.html`), к фиче не относятся. Git-операций агент не выполнял.

## Ручная приёмка — чек-лист для человека (против живого стенда исполнителем не выполнялась)

Подготовка: `./up.dev.sh` (compose `-p receipt-collector-dev`); админ и обычный
пользователь; `AI__*` настроены; миграция применена по
`docs/runbooks/commodity-name-normalization-deploy.md`. Точка отсчёта:
`SELECT status, count(*) FROM precategorized_commodities GROUP BY status;`
(сразу после миграции `status = 0` — 0).

1. **Чек (UC-1/UC-2).** Чек с позицией `2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ` →
   «Предложить категории». Ожидание: в ИИ уходит очищенное `пирог с вишней по ре`
   (в логах Analytics Warning/Info — **сырое** имя, это FR-4), ответ `source = "ai"`,
   `name` в UI сырое. Сохранить категорию. Позиция/чек `ПИРОГ С ВИШНЕЙ ПО РЕ` (без кода)
   → suggest → `source = "cache"` (или `precategorized`), **повторного вызова ИИ нет**
   (логи AI-клиента). SQL:
   `SELECT name, normalized_name FROM precategorized_commodities WHERE normalized_name = 'пирог с вишней по ре';`
   — если строка есть, ключ очищенный.
2. **«Товары» (UC-3).** Отметить `2195738 Туалетная бумага "СВАЁ" (3сл, 4р) 4810981001530`
   и `Туалетная бумага "СВАЁ" (3сл, 4р)` в одной порции → suggest: одинаковая категория и
   источник у обоих, **один** вызов ИИ; сохранение assign-only как раньше (`updated` по
   реально изменённым строкам).
3. **Очередь (FR-3, NFR-1).** Suggest позиции с кодом находит строку очереди по очищенному
   ключу (`source = precategorized`). В `/precategorized` UI показывает **сырые** имена.
   Дубли `proposed` по одному ключу отсутствуют (partial unique):
   `SELECT normalized_name, count(*) FROM precategorized_commodities WHERE status = 0 GROUP BY 1 HAVING count(*) > 1;`
   — пусто. Confirm строки с кодом → категорию получили только `commodities` с **точным
   сырым** именем, вариант без кода — без категории:
   `SELECT name, category_id FROM commodities WHERE name IN ('2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ', 'ПИРОГ С ВИШНЕЙ ПО РЕ');`
4. **Миграция + фон (FR-5).** Сразу после миграции (до рабочего окна фона): запросы 1–4
   runbook дают 0 / 0 / «только status=1 ≥ дорелизного» / 0 (с оговоркой про fallback
   «только цифры ≥6»). После окна `Precategorization__WorkWindow*` (ADR-030) фон пишет
   новые `proposed` с очищенным `normalized_name`; у варианта с кодом повторного
   предложения поверх существующего ключа нет:
   `SELECT name, normalized_name FROM precategorized_commodities WHERE status = 0 ORDER BY created_at DESC LIMIT 10;`
5. **Журнал (FR-4).** Suggest + сохранение на чеке с именем с EAN → новое событие:
   `SELECT commodity_name, normalized_name FROM categorization_decisions ORDER BY created_at DESC LIMIT 5;`
   — `commodity_name` сырое, `normalized_name` очищен. История не пересчитана: счётчик и
   старые строки совпадают с «до» (`SELECT count(*) FROM categorization_decisions;`).
6. **Контракты (NFR-2).** При пустом `AI__BaseUrl`: чек-suggest → `503`; страница
   «Товары» → `200` + `aiAvailable = false`. Коды `400/401/403/404/409` и `updated`
   не изменились; автопроставлений нет (категории появляются только после действий
   пользователя/админа).

## Сверка требований: критерий приёмки → чем подтверждён

| Критерий / требование | Подтверждение |
|---|---|
| Юнит-тесты нормализатора: 4 примера, границы (`АИ-95`, `1000 ЛЕПЕСТКОВ`, 5/6 цифр), fallback «только код» | Авто: `CommodityNameNormalizerTests.NormalizeName_applies_canonical_cleaning_rule` (golden corpus 26 кейсов) + факты (детерминизм, tr-TR, `null`) — T-01/T-03 |
| В ИИ уходит очищенное имя (чек, «Товары», фон) | Авто: `Resolve_sends_cleaned_name_to_ai`, `Suggest_sends_cleaned_name_to_ai_and_returns_raw_name`, `Ai_and_precheck_receive_cleaned_name_and_proposal_keeps_raw_name` (захват аргумента) — T-05/T-06/T-07; ручная — п. 1, 2 |
| Варианты с кодом/без → один ключ кэша/дедупликации; второе предложение из кэша/очереди, не от ИИ | Авто: `Resolve_deduplicates_variants_with_and_without_code_into_one_ai_call`, `Suggest_variants_with_and_without_leading_code_share_one_ai_call`, `Suggest_variant_without_code_comes_from_cache_on_cleaned_key_without_second_ai_call`, `Apply_stores_cleaned_name_in_cache_for_raw_name_with_leading_code` — T-05/T-07; ручная — п. 1, 2 |
| Поиск по очереди по очищенному ключу | Авто: `HasBlockingQueueEntryAsync_matches_by_cleaned_key`, `GetCategoriesByNormalizedNamesAsync_matches_row_by_cleaned_key`, `Resolve_finds_queue_row_by_cleaned_key_and_skips_ai` — T-04/T-05; ручная — п. 3 |
| После миграции нет `proposed`; `applied` сохранены и backfill'ены; фон пересобирает | Авто: `PrecategorizedNormalizedNameMigrationTests` (реальный файл скрипта, весь corpus) — T-03; смоук контрольных запросов 1–6 (этот отчёт, подтверждён ревьюером); ручная — п. 4 |
| Журнал: сырое `commodity_name` + очищенный `normalized_name`; история не пересчитана | Авто: `RecordAsync_name_with_leading_ean_keeps_raw_name_and_cleans_normalized_name`, `RecordAsync_code_only_name_gets_non_empty_fallback_within_column_guard`, `RecordAsync_max_length_name_with_leading_code_is_written_with_shorter_normalized_name`, `RecordAsync_variants_with_and_without_code_share_normalized_name_but_not_raw_name` (T-08) + отсутствие `UPDATE categorization_decisions` в диффе фичи; ручная — п. 5 |
| `cd Analytics && dotnet test` зелёный | Прогон 635/635 (исполнитель и ревьюер независимо) |
| Интерфейс и API-контракты не изменены; автопроставления нет (NFR-1, NFR-2) | Авто: `ConfirmAsync_updates_commodities_only_by_exact_raw_name`, `ConfirmAsync_updates_commodities_of_all_users_and_touches_only_confirmed_row`, `Confirm_updates_two_users_marks_applied_and_touches_only_confirmed_row_and_fills_cache` (T-04); `Suggest_returns_503_when_ai_is_not_configured`, `Suggest_without_ai_base_url_keeps_undefined_outcome_for_code_prefixed_name`, `Suggest_without_ai_base_url_returns_200_compatible_partial_result_and_ai_available_false` (контракты 503/200+aiAvailable); `Suggest_counts_max_unique_names_by_raw_names_not_cleaned_keys`, `Suggest_allows_20_raw_variants_of_one_cleaned_name_and_calls_ai_once` (лимит по сырым); ручная — п. 3, 6 |
| FR-1…FR-5 | Матрица ADR-034 п. 7 + трассировка плана; покрыты строками выше (FR-5 — паритетный тест + смоук + runbook) |
| NFR-3 (детерминизм, без побочных эффектов, накладные расходы) | Авто: тесты детерминизма/культуры нормализатора (T-01); один индексируемый pre-check на кандидата и один batch-запрос (код T-04/T-05, подтверждён ревью T-04/T-05) |
| NFR-4 (существующие тесты зелёные, пустые/битые имена не ухудшились) | Прогон 635/635; существующие кейсы (`""`, `null`, `Молоко 2.5%` vs `3.2%`, пунктуация) без изменения ожиданий (T-01) |
| Edge cases задачи («только код», короткие числа, код в середине, `*#№`, кластеры) | Авто: golden corpus в обоих реализациях — C# (`NormalizeName_applies_canonical_cleaning_rule`) и SQL-backfill (паритетный тест); ручная — п. 4 (fallback виден в очереди) |
| Границы (нет изменений вне Analytics) | `git diff HEAD` по `backend/`, `bot/`, `docker-compose*.yml`, `.env*`, `AGENTS.md`, `Analytics/frontend/` — пусто |

## Остаточные риски

1. **Открытые вопросы ADR-034 1–5 и 7 не решались** (вне объёма плана; текущая трактовка —
   ADR-034 п. 1/п. 5, изменения только отдельным решением): 1 — буквальный `*3455614`
   (сейчас fallback на `base`); 2 — политика коллизий кэша (first-wins сохранён);
   3 — массовое применение при confirm (не делается); 4 — глобальный `existing` фона
   остаётся по сырому имени (зафиксированное ограничение, кэш смягчает); 5 — разбитые коды
   (`241-7268000006`, `241 7268000006`) вне правила; 7 — показ `normalized_name`
   в админке (UI показывает сырые имена). Вопрос 6 решён этим планом (паритетный тест +
   контрольные запросы). **Вопрос 8 (класс букв `[A-Za-zА-Яа-яЁё]`) принят как есть**:
   имена целиком из вне-классовых букв уходят в fallback и сохраняют код; расширение
   класса — отдельное решение с оценкой ложных срабатываний.
2. **Коллизии кэша first-wins.** Разные товары с одним очищенным ключом
   (`000001 пирог` vs `000002 пирог`) схлопываются; при старте побеждает первая строка
   `OrderBy(Name)`. Смягчение: подтверждения человеком, автоприменения нет; исправление —
   правка категории/перезапуск Analytics.
3. **Флейк Testcontainers `mongo:7.0`** (`Address already in use`/зависание) в CI-среде:
   при проявлении — `docker ps -a`, удалить мёртвые контейнеры **без** `-v`, повторить
   прогон; не регрессия кода.
4. **Локальный дрейф `appsettings*.json` / `wwwroot/index.html`** — личные значения
   (AI-endpoint, Telegram-ID) и локальная сборка SPA; в коммиты не включать никогда.
5. **Preflight `RAISE EXCEPTION` тестом не покрыт** (расхождение `lower()` PostgreSQL vs
   `ToLowerInvariant()` .NET 10 ICU невоспроизводимо — факт зафиксирован тестом T-02
   `TryAddProposalAsync_normalized_name_never_exceeds_raw_length_in_current_runtime`);
   остаётся страховкой на реальных данных и проверяется только на dev. **Оговорка к
   контрольному запросу 4**: легитимное исключение — fallback-строки, имя которых состоит
   только из цифр ≥6 (например `2417268000006`): по ADR-034 п. 1 они сохраняют код в ключе
   и матчатся regex'ом; при `count > 0` просмотреть возвращённые строки (тот же запрос без
   `count`) до решения об остановке — внесено в runbook.
6. **Ручная приёмка на dev-стенде исполнителем не выполнена** (стенд недоступен) —
   чек-лист выше выполняет человек; без него критерии «ручная проверка» плана T-09
   считаются закрытыми только автотестами и смоуком на временном postgres.

## Заметки для коммита

Сообщение: `T-09 final verification + deploy runbook`.

В коммит включить **только**:

- [ ] `docs/runbooks/commodity-name-normalization-deploy.md` — новый runbook;
- [ ] `docs/reviews/commodity-name-normalization/09-final-verification.md` — эта сводка;
- [ ] `docs/plans/commodity-name-normalization.md` — строки «Ход выполнения»: T-08
      (`approved → committed`, bookkeeping за коммит `6c2021e`) и T-09
      (`pending → ... → approved`).

**Не включать**: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` — предсуществующие
локальные артефакты.

---

Декомпозиция завершена: T-01…T-09 (перевод задачи в `completed` — за @project-manager).
Применение миграции на dev/prod — по `docs/runbooks/commodity-name-normalization-deploy.md`.
