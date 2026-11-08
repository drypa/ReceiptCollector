# Задача 12 - Финальная проверка контрактов и регрессии

- **Задача**: docs/plans/categorization-decision-log.md#t-12-финальная-проверка-контрактов-и-регрессии
- **Дата**: 2026-11-08
- **Ревью**: раундов - 1, вердикт APPROVED (блокеров нет)

## Что сделано

Финальная проверочная задача фичи «журнал решений категоризации»: подтверждена работа
сквозного сценария и неизменность существующих контрактов и пользовательского потока
(NFR-1/2/3), соответствие реализации ADR-033 и amend-заметкам ADR-009/027/028.
**Код не изменялся** — задача сводилась к прогонам автотестов, статической сверке контрактов
и семантик с ссылками файл:строка, ручной сверке SQL ↔ EF (перенесена из T-02, без нового
автотеста — план T-12 запрещает добавлять тесты) и проверке гигиены вне Analytics.
Новых функций, тестов и правок архитектуры не вносилось; несоответствий не найдено,
эскалаций нет.

## Изменённые файлы

- `docs/plans/categorization-decision-log.md` - только статус T-12 в таблице «Ход выполнения»
  (`pending` → `in-progress` → `review` → `approved`).
- `docs/reviews/categorization-decision-log/12-final-verification.md` - эта сводка.

## Как проверено

### Прогоны (фактические результаты)

| Команда | Результат разработчика | Подтверждение ревьюера |
|---|---|---|
| `docker ps -a` перед тестами | осиротевших `mongo:7.0`/ryuk нет, порт 27017 свободен, `rc-mig-verify` не тронут | - |
| `cd Analytics && dotnet test` | **Passed: 577, Failed: 0, Skipped: 0** (1 m 4 s) - базлайн T-08/T-10 | независимо **577 / 0 / 0** |
| `dotnet test --filter ProjectDependencyTests` | **4 / 4 passed** (`Architecture/ProjectDependencyTests.cs:10` внутри Api.Tests-сборки) | **4 / 4** |
| `cd Analytics/frontend && npm run lint` | чисто (`eslint . --max-warnings 0`) | зелёные |
| `npm run build` | `tsc -b && vite build` успешно (артефакты в `wwwroot` - локальные, не для коммита) | зелёные |

### ADR-033 «Явно зафиксированные семантики»: 21 / 21 (уточнение счётчика по ревьюеру: в таблице 21 строка)

| Семантика | Код | Тесты |
|---|---|---|
| Единица события: чек/«Товары» - позиция; `/precategorized` - строка очереди | `CommodityCategorizationService.cs:119-129`; `CommodityBatchCategorizationService.cs:288-302`; `PrecategorizedCommodityService.cs:116-125` | `CommodityCategorizationServiceTests:480,512`; `CommodityBatchCategorizationServiceTests:502,536`; `PrecategorizedCommodityServiceTests:54,163`; workflow `:137,183,220` |
| Допустимые источники - 4 wire-имени | `SuggestionSnapshotParser.cs:59-66`; recorder перевалидирует | `CategorizationDecisionRecorderTests:59,110` |
| Нет снимка - нет события (FR-5b) | `CommodityCategorizationService.cs:119`; `CommodityBatchCategorizationService.cs:275-282` | `...does_not_record_without_snapshot:540/:574`; `..._with_partial_snapshot:592` |
| Предложение показали, не сохранили - событий нет | ровно 3 точки вызова `RecordAsync` - все save-пути; в `SuggestForReceiptAsync:50-71` и `SuggestAsync:60-157` recorder не вызывается | - |
| Автоприменения - событий нет | фон ADR-027 пишет только очередь; merchant-default (ADR-032) без recorder | `PrecategorizedCommodityRepositoryTests:209,538` |
| Одиночный редактор `PUT /api/commodities/{id}/category` - не точка фиксации | `CommodityEndpoints.cs:62-111` - recorder не входит в зависимости эндпоинта | `CommodityEndpointsTests:132-212` |
| Сброс в «Не указана» → `final = 0` | `CommodityCategorizationService.cs:126` (`?? Undefined`) | `...reset_after_suggestion_records_undefined_final_category:556` |
| `final NULL` vs `0` - канон 0 | CHECK `final_category_id >= 0` (SQL `:66-67`; EF `:21-23`) | `CategorizationDecisionRepositoryTests:186` |
| Ключ сочетания 4 поля; «Хлеб»/«ХЛЕБ» - разные строки | `CategorizationDecisionRepository.cs:126-129` (по сырому `CommodityName`) | `CategorizationDecisionRepositoryTests:100,219` |
| «Кто принял» и `decidedAt` - от последнего события | anti-join сохраняет свежайшее; tie-break `(created_at, id)` (`:130-131`) | `CategorizationDecisionReadServiceTests:23` |
| Период и дедуп: фильтр → дедуп | `BuildDeduplicatedQuery:119-131` - фильтры до anti-join, корреляция по отфильтрованному набору | read `:166`; Postgres `...period_and_onlyMismatch_apply_before_dedup_on_postgres:382` |
| Имя принявшего - JOIN на чтении; недоступно → null; id/флаг всегда | `CategorizationDecisionReadService.cs:60-68`; явный JSON null на эндпоинте | `...missing_or_nameless_user...null_name:192`; `...null_accepted_by_name_as_explicit_json_null:314` |
| Admin-флаг - снимок `users.is_admin` на момент решения | чек: `ReceiptCategorizationEndpoints.cs:148-155` (одно чтение, только при валидном снимке - `:416/:447`); «Товары»/очередь - `true` (admin-only) | `...non_admin_decision_preserves_admin_snapshot:80`; `...passes_accepted_by_admin_to_draft:607` |
| FK `accepted_by → users` осознанно нет | SQL `:53-68`; в EF навигации нет | `CategorizationDecisionMappingTests:19,48` |
| `commodity_id`/`receipt_id` не хранятся | набор колонок SQL и `CategorizationDecisionEntity.cs` | `...Model_MatchesMigrationColumnsAndLengths:48` |
| Невалидный снимок - Warning (только количество), коды прежние | `ReceiptCategorizationEndpoints.cs:138-146`; `CommodityBatchCategorizationEndpoints.cs:194-200` | `Save_ignores_invalid_snapshot_without_bad_request:380`; `Save_with_invalid_or_partial_snapshot_returns_200_and_drops_it:514` |
| Ошибка журнала - Error, сохранение успешно (NFR-3) | recorder never-throws + оборонительные `catch` в трёх сервисах (`:150-165` / `:314-329` / `:143-158`) | `_repository_failure_is_swallowed:219`; `_cancellation_is_swallowed:232`; `*_ignores_recorder_failure:709/:695/:197` |
| Строго после commit, один батч-INSERT на запрос | `AddRangeAsync` = один `SaveChangesAsync` (`CategorizationDecisionRepository.cs:35-38`) | `...records_only_after_category_is_saved:661`; наблюдение «save+cache → recorder» `:663-684`; `ConfirmAsync_records_only_after_repository_transaction_applied:87`; `...single_repository_call:200` |
| `suggestion_source` без DB-CHECK - множество в коде | SQL CHECK'а нет; EF `HasMaxLength(32)` | `RecordAsync_source_outside_allowed_set_is_skipped:110` |
| Новые настройки отсутствуют | секции журнала в `appsettings.json` нет | - |
| Single instance достаточно; write-поверхности нет | `CategorizationDecisionEndpoints.cs:44-45` - только `MapGet`; `AdminAuthorization` первым (`:78-82`) | `...without_user_returns_unauthorized:33`; `...non_admin_returns_forbidden_and_data_never_loaded:42` |

### Amend-заметки ADR-009 / 027 / 028

- **ADR-009** (`:28-35`): `PUT /api/receipts/{id}/categories` - аддитивные опциональные
  `suggestedCategory`/`suggestionSource`, без полей поведение прежнее, suggest не менялся -
  соответствует `ReceiptCategorizationEndpoints.cs`.
- **ADR-027** (`:36-44`): одно событие `precategorized` после commit транзакции,
  `suggested = original_proposed_category_id`, `final =` текущий `proposed_category_id`;
  `PUT .../category` колонку не трогает; DELETE без событий - соответствует
  `PrecategorizedCommodityService.ConfirmAsync` и тестам workflow `:137,183,220`.
- **ADR-028** (`:25-33`): assign-only (C4), `updated` (C9), коды и поведение при пустом
  `AI:BaseUrl` (C7) без изменений; `DraftEntry` со несменяемым снимком (T-10) - соответствует.

### NFR-2 (аддитивность контрактов) и NFR-3 (независимость от журнала)

- Чек: `401` (`ReceiptCategorizationEndpointsTests:101`), `400` прежние тексты
  (`:119-213`), `404` (`:213`), `updated` (`:265`); `SaveCategoryItemRequest` - опциональные
  поля `= null` (`ReceiptCategorizationEndpoints.cs:184-188`).
- «Товары»: `401/403` (`CommodityBatchCategorizationEndpointsTests:248-276`), `400` (`:278-343`),
  `409` (`Save_maps_not_accessible_exception_to_conflict:426`), `updated` (`:406`).
- `suggest` при пустом `AI:BaseUrl`: чек `503` (`Suggest_returns_503_when_ai_is_not_configured:42`),
  «Товары» `200 + aiAvailable = false` (`Suggest_without_ai_configuration_returns_200_not_503:166`).
- Отсутствие снимка (одиночный редактор, ручная установка без suggest) - событий нет
  (`Save_does_not_query_users_when_no_snapshot_is_present:416`; репо-зависимости эндпоинта).
- NFR-3: строка «Ошибка журнала» таблицы семантик + тесты трёх сервисов выше.

### SQL ↔ EF (ручная сверка; автодрейф-тест сознательно НЕ добавлен - план T-12)

`20261106120000_add_categorization_decisions.sql` ↔ `CategorizationDecisionConfiguration.cs` /
`CategorizationDecisionEntity.cs` / `PrecategorizedCommodityConfiguration.cs`
(`UseSnakeCaseNamingConvention` - `DependencyInjectionExtensions.cs:146`):

| Объект | SQL | EF | Вердикт |
|---|---|---|---|
| Таблица | `categorization_decisions` | `ToTable(...)` | совпадает |
| `id` | uuid PK | `HasKey`, Guid→uuid | совпадает (имя PK: автоимя `..._pkey` vs EF `pk_...` - принятый общепроектный стиль, сводка T-02; ревьюер подтвердил: не дрейф) |
| `commodity_name` / `normalized_name` | varchar(256) NOT NULL | `IsRequired` + `HasMaxLength(256)` | совпадает |
| `suggestion_source` | varchar(32) NOT NULL, CHECK нет | `IsRequired` + 32, CHECK нет | совпадает |
| `suggested_category_id` / `final_category_id` | integer NOT NULL | `int` + `IsRequired` | совпадает |
| `accepted_by` | uuid NOT NULL, FK нет | `Guid` + `IsRequired`, навигации нет | совпадает |
| `accepted_by_admin` | boolean NOT NULL | `bool` + `IsRequired` | совпадает |
| `created_at` | timestamptz NOT NULL DEFAULT now() | `HasColumnType("timestamp with time zone")` + `HasDefaultValueSql("now()")` | совпадает |
| CHECK ×2 | `ck_categorization_decisions_suggested_not_undefined` (`<> 0`), `ck_categorization_decisions_final_non_negative` (`>= 0`) | те же имена и выражения (`:18-23`) | совпадает |
| Индексы ×3 | combination (4 кол. + `created_at DESC, id DESC`); `created_at DESC`; частичный mismatch `WHERE suggested <> final` | `IsDescending(false,×4,true,true)`; `IsDescending(true)`; `HasFilter(...)` + `HasDatabaseName` (`:61-86`) | совпадает |
| Очередь | `original_proposed_category_id` integer + backfill + `SET NOT NULL` | `OriginalProposedCategoryId` `IsRequired` (`:29-30`) | совпадает |

**Дрейфа нет.** Форма EF-модели дополнительно закреплена существующими тестами T-02
(`CategorizationDecisionMappingTests:48,88,118`; контейнерный
`CategorizationDecisionRepositoryTests.Model_creates_check_constraints_and_all_three_indexes:126`),
однако они не парсят `.sql`-скрипт - см. остаточный риск №1.

### Гигиена вне Analytics

`git status --short` / `git diff --stat HEAD`: изменены только предсуществующие локальные
артефакты (`appsettings.json`, `appsettings.Development.json`, `wwwroot/index.html`) и план
(статусы). `backend/`, `bot/`, `docker-compose*.yml`, `.env*`, `AGENTS.md` - не тронуты;
`Analytics/frontend/package.json` и все `*.csproj` - без diff относительно HEAD.

## Ручная приёмка - чек-лист для человека (против живой БД не выполнялась)

Подготовка: `./up.dev.sh`; админ-пользователь и обычный пользователь; `AI__*` настроены;
роль приложения в PostgreSQL - `<роль analytics>` (см. `POSTGRES_USER` / connection string).
Точка отсчёта: `SELECT count(*) FROM categorization_decisions;` (запомнить N0).

1. **Чек**: открыть чек → «Предложить категории» → позицию A согласиться, позицию B сменить,
   позицию C сбросить в «—» → Сохранить (ожидание - обычный тост успеха, UI не менялся):
   `SELECT commodity_name, suggestion_source, suggested_category_id, final_category_id, accepted_by_admin FROM categorization_decisions ORDER BY created_at DESC LIMIT 5;`
   - 3 новых события; у A `suggested_category_id = final_category_id`; у B - различаются;
   - **у C `final_category_id = 0`**; источник - один из показанных
     (`ai`/`precategorized`/`cache`/`merchant-default`).
2. **«Товары»**: отметить ≤5 строк → suggest → сохранить: события появились только по реально
   изменённым строкам; несохранённые/неизменённые позиции в журнале отсутствуют
   (`SELECT count(*) ...` выросла ровно на число изменённых снимков).
3. **`/precategorized`**: правка предложения (`PUT /api/precategorized-commodities/{id}/category`)
   → confirm. SQL-подтверждение (сверка с очередью):
   `SELECT d.suggestion_source, d.suggested_category_id, d.final_category_id, p.original_proposed_category_id, p.proposed_category_id FROM categorization_decisions d JOIN precategorized_commodities p ON p.name = d.commodity_name WHERE d.suggestion_source = 'precategorized' ORDER BY d.created_at DESC LIMIT 1;`
   - **`suggestion_source = 'precategorized' AND suggested_category_id = original_proposed_category_id`**,
     `final_category_id` = исправленное значение.
   Confirm без правки → `suggested = final` (согласие). Отклонение (DELETE) → нового события нет.
4. **Журнал `/categorization-decisions`** (под админом): фильтр «только расхождения»
   (согласия скрыты), источник, обе категории, поиск (регистронезависимый, по исходному и
   нормализованному имени), период (`from` включительно, `to` исключительно), пагинация;
   две одинаковые комбинации - одна строка, «кто принял» от последнего события. В psql
   сверить `X-Total-Count` с числом уникальных сочетаний под тем же фильтром, например:
   `SELECT count(*) FROM (SELECT DISTINCT commodity_name, suggestion_source, suggested_category_id, final_category_id FROM categorization_decisions) t;`
5. **Не-админ**: страницы нет в навигации;
   `curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer <токен юзера>" 'http://localhost/api/categorization-decisions'` → `403`;
   без токена → `401`.
6. **Негатив NFR-3 (отзыв INSERT)**:
   `REVOKE INSERT ON categorization_decisions FROM <роль analytics>;`
   → сохранить категорию на чеке → ожидаемо **`200`** (обычный ответ сохранения) и `Error`
   «...decision log...» в логе Analytics (`docker compose logs analytics`);
   вернуть: `GRANT INSERT ON categorization_decisions TO <роль analytics>;`
   → следующее сохранение снова пишет события (`SELECT count(*) ...` растёт).

## Итоги ревью

- Вердикт @code-reviewer: **APPROVED**, раунд 1, блокеров нет.
- Ревьюер независимо подтвердил прогоны (577/0/0, `ProjectDependencyTests` 4/4, фронт
  lint/build зелёные) и перечитал 12+ заказанных пунктов семантик ADR-033: ровно 3 точки
  вызова `RecordAsync`; одиночный редактор журнал не пишет; фильтры до anti-join, корреляция
  по отфильтрованному набору, `Total` из того же запроса (с контейнерным тестом); `/precategorized` -
  событие и при `updated = 0`, `suggested = OriginalProposedCategory`; запись строго после
  commit одним `AddRangeAsync`; never-throws recorder + оборонительные catch в трёх сервисах
  + тесты; парсер снимка; сброс → `final = 0`; admin-флаг одним чтением только при снимке;
  write-поверхности нет; FK нет.
- SQL ↔ EF перепроверено ревьюером лично (7 позиций, включая имена/выражения CHECK,
  `IsDescending`, частичный индекс, `timestamptz DEFAULT now()`, `SET NOT NULL` после
  backfill); PK-имя - общепроектный стиль, не дрейф.
- Уточнение по счётчику (принято в эту сводку): в таблице ADR-033 **21** строка семантик
  (не 20) - подтверждено 21/21.
- Охват §T-12 полный; «Что НЕ входит» в дифф не просочилось; `git diff HEAD` - только план.

## Остаточные риски

1. **Автоматический дрейф-тест SQL ↔ EF не добавлен** - план T-12 запрещал новые тесты;
   сверка выполнена вручную (дрейфа нет), EF-модель частично закреплена тестами T-02, но
   парсинг `Scripts/*.sql` не автоматизирован. Рекомендация - отдельная мини-задача вне
   текущей декомпозиции (сравнение `EnsureCreated`-схемы с прогнанными скриптами на
   Testcontainers).
2. **Производительность anti-join на росте таблицы** - замеры планов на реальных объёмах
   (ADR-033 открытые вопросы 1-2) — эксплуатационная задача; при деградации - запас `DISTINCT ON`
   raw SQL (E2).
3. **Политика ретеншена журнала не определена** - растущая таблица без TTL/архивации;
   открытая область следующей фичи (открытый вопрос 1 ADR-033).
4. **Ручная приёмка FR-6/FR-7 и сквозных сценариев не выполнена** - чек-лист выше; требует
   живой среды (`./up.dev.sh`) и действий человека.
5. **DRY: тройная копия best-effort helperа recorder** в трёх сервисах - рефакторинг
   сознательно отложен после T-12, принято ревью T-04/T-05/T-06.

## Заметки для коммита

Сообщение вида: `T-12 final verification`
(например: `T-12 final verification: contracts, regression, SQL-EF drift - approved`).

В коммит включить **только**:

- [ ] `docs/plans/categorization-decision-log.md` - строки «Ход выполнения»: T-11
      `approved → committed` и T-12 `pending → ... → approved`;
- [ ] `docs/reviews/categorization-decision-log/12-final-verification.md` - эта сводка.

**Не включать**: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` - предсуществующие
локальные артефакты/сборка.

---

Декомпозиция завершена: T-01…T-12 (финальная валидация конвейера и перенос задачи в
`completed` - за @project-manager).
