# План: In-memory кэш категорий товаров (замена `commodity_category_assignments`)

## Описание задачи

Реализовать замену кэша ранее присвоенных категорий по [задаче in-memory-commodity-category-cache](../tasks/in-memory-commodity-category-cache.md)
в соответствии с архитектурным решением [ADR 019](../adr/019-in-memory-commodity-category-cache.md).

Суть: вместо PostgreSQL-таблицы `commodity_category_assignments` (ADR 009, решение C1/C4) вводится
**in-memory кэш в Analytics** — ключ-значение «нормализованное название товара → `CommodityCategory`»,
наполняется при старте из `commodities` (категория ≠ `Undefined`), обновляется при подтверждении категоризации
и ручном `PUT /api/commodities/{id}/category`, имеет настраиваемый лимит (по умолчанию 1000, «замерзание» без
вытеснения). Таблица удаляется миграцией.

Ключевые требования и ограничения:
- Приоритеты алгоритма **не меняются**: «существующая категория позиции → in-memory кэш → ИИ» (FR-2.1);
- при совпадении с кэшем ИИ **не вызывается** (FR-2.2) — устраняется наблюдение заказчика «запись есть,
  а ИИ всё равно вызывается» (причины зафиксированы в ADR 019, раздел «Анализ причины»);
- масштаб кэша — **сквозной для всех пользователей** (без `user_id`);
- семантика записи — **first-wins** (FR-1.4: первое значение выигрывает; конфликты — в бэклог);
- лимит: `CommodityCategoryCache:MaxSize` (env `CommodityCategoryCache__MaxSize`, по умолчанию 1000),
  при достижении лимита кэш «замерзает» (FR-1.5);
- webUI (`Analytics/frontend`), Backend (Go), Telegram-бот, nginx, порт `5039` — **не изменяются**;
- MongoDB остаётся read-only.

## Задачи

### 1. Контракт и реализация кэша (Application + Infrastructure)

**Описание:**
- Новый контракт `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategoryCache.cs`
  (методы `TryGet(normalizedName, out category)`, `TryAdd(normalizedName, category)`, `Count`). Контракт в
  Application (кэш — прикладной сервис; слои по `ProjectDependencyTests` не нарушаются).
- Новая реализация `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/InMemoryCommodityCategoryCache.cs`:
  `ConcurrentDictionary<string, CommodityCategory>`; `TryAdd` — first-wins (ключ уже есть → `false`),
  лимит (`Count >= MaxSize` → `false`, без вытеснения, «замерзание»), проверка лимита и гонки ключей — под
  атомарной блокировкой; `CommodityCategory.Undefined` не добавляется (защита на уровне реализации).
- Удалить из Domain: `CommodityCategoryAssignment.cs`, `ICategoryAssignmentRepository.cs`.
  `CommodityNameNormalizer.cs` — остаётся.

**Затрагиваемые сервисы/файлы:** `Analytics/src/.../Application/Modules/Commodities/Contracts/` (новый),
`Analytics/src/.../Infrastructure/Modules/Commodities/` (новый), `Analytics/src/.../Domain/Modules/Commodities/` (удаления).

**Зависимости:** нет.

**Приоритет:** P0
**Оценка времени:** 0.5 дня

### 2. Конфигурация лимита и DI (Infrastructure)

**Описание:**
- Новый `Infrastructure/Configuration/Options/CommodityCategoryCacheOptions.cs`:
  секция `CommodityCategoryCache`, свойство `MaxSize` (int, по умолчанию 1000); env `CommodityCategoryCache__MaxSize`
  (стиль `AI__*`, без пересборки — NFR-1 подхода).
- Биндинг в `DependencyInjectionExtensions.ConfigureInfrastructureOptions`
  (`services.AddOptions<CommodityCategoryCacheOptions>().Bind(...GetSection(...))`).
- DI-регистрация: `services.AddSingleton<ICommodityCategoryCache>(sp => new InMemoryCommodityCategoryCache(
  sp.GetRequiredService<IOptions<CommodityCategoryCacheOptions>>().Value.MaxSize));`.
- `MaxSize < 1` трактуется как «кэш отключён» (кэш не наполняется, ИИ вызывается всегда — документированный
  edge case).

**Затрагиваемые сервисы/файлы:** `Analytics/src/.../Infrastructure/Configuration/Options/` (новый),
`Configuration/DependencyInjectionExtensions.cs`, `Api/appsettings.json` и `appsettings.Development.json`
(секция `CommodityCategoryCache`).

**Зависимости:** задача 1.

**Приоритет:** P0
**Оценка времени:** 0.5 дня

### 3. Наполнение кэша при старте (FR-1.2, FR-1.7; Infrastructure)

**Описание:**
- Новый `Infrastructure/Synchronization/CommodityCategoryCacheInitializationHostedService.cs`
  (паттерн `ReceiptSynchronizationHostedService`: `IServiceScopeFactory` → scoped `ReceiptDbContext`):
  1. `StartAsync`: scope, `ReceiptDbContext`;
  2. выборка `Commodities.AsNoTracking().Where(c => c.CategoryId != null && c.CategoryId != 0)`
     (категория заполнена и ≠ `Undefined`; `OrderBy(c => c.Name)` — детерминизм first-wins при равных названиях;
     проекция только `(Name, CategoryId)`);
  3. для каждой позиции: пропуск невалидных `CategoryId` (`Enum.IsDefined`),
     ключ = `CommodityNameNormalizer.NormalizeName(Name)`, `TryAdd(key, (CommodityCategory)CategoryId)`;
  4. **ранний выход** при `Count == MaxSize` (лимит достигнут — дальнейшие `TryAdd` были бы no-op, first-wins в
     алфавитном порядке даёт тот же результат);
  5. лог `Count`; ошибка подключения/запроса — `throw` (fail-fast, как у синхронизации).
  Важно: query filter `ReceiptDbContext` применяется только к `ReceiptEntity` — запрос наполнения охватывает
  позиции **всех** пользователей (сквозной кэш FR-2.4), фильтр по `userId` не добавлять.
- Регистрация `services.AddHostedService<CommodityCategoryCacheInitializationHostedService>()` в
  `DependencyInjectionExtensions.AddInfrastructure`.
- Порядок: hosted-сервисы завершают `StartAsync` до приёма HTTP-запросов → кэш наполнен до первого `suggest`.

**Затрагиваемые сервисы/файлы:** `Analytics/src/.../Infrastructure/Synchronization/` (новый),
`Configuration/DependencyInjectionExtensions.cs`.

**Зависимости:** задачи 1, 2.

**Приоритет:** P0
**Оценка времени:** 1 день

### 4. Подключение кэша в сервис категоризации (FR-2.1…FR-2.4; Infrastructure)

**Описание:**
- `CommodityCategorizationService`: зависимость `ICategoryAssignmentRepository` → `ICommodityCategoryCache`.
- Шаг 2 `SuggestForReceiptAsync`: `_cache.TryGet(CommodityNameNormalizer.NormalizeName(item.Name), out var category)`
  → `source = cache`, **ИИ не вызывается** (FR-2.2; фиксируется автотестом со сценарием «АИ-95-К5»).
  Шаги 1 и 3 алгоритма, дедупликация AI-вызовов, частичный успех (UC-4) — без изменений.
- `ApplyConfirmedCategoriesAsync`: после `UpdateCategoryAsync` для позиций с назначенной категорией —
  `_cache.TryAdd(normalized, category)` (FR-1.3). Сброс (`null`) в кэш не пишется.

**Затрагиваемые сервисы/файлы:** `Analytics/src/.../Infrastructure/Modules/Commodities/CommodityCategorizationService.cs`.

**Зависимости:** задачи 1–3.

**Приоритет:** P0
**Оценка времени:** 1 день

### 5. Ручное назначение категории обновляет кэш (FR-1.3; Api)

**Описание:**
- `CommodityEndpoints.UpdateCategory` (`PUT /api/commodities/{id}/category`): добавить
  `[FromServices] ICommodityCategoryCache cache`; после `commodityRepository.UpdateCategoryAsync(id, category, ct)`
  для `category != CommodityCategory.Undefined` — `cache.TryAdd(CommodityNameNormalizer.NormalizeName(commodity.Name), category)`
  (`commodity` уже получен в методе; доп. запросов нет).
- Это устраняет причину 1 из ADR 019 (ручная категоризация не попадала в кэш).

**Затрагиваемые сервисы/файлы:** `Analytics/src/.../Api/Modules/Commodities/CommodityEndpoints.cs`.

**Зависимости:** задачи 1, 2.

**Приоритет:** P0
**Оценка времени:** 0.5 дня

### 6. Удаление таблицы и EF/доменного кода (FR-1.6; Migrations + Infrastructure + Domain)

**Описание:**
- Новый SQL-скрипт `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20260922120000_drop_commodity_category_assignments.sql`:
  `DROP TABLE IF EXISTS commodity_category_assignments;` (`IF EXISTS` — среда без применённой add-миграции
  не сломается). Данные не переносятся (источник истины — `commodities`, FR-1.7).
- Удалить код, обращающийся к таблице:
  - Domain: `CommodityCategoryAssignment.cs`, `ICategoryAssignmentRepository.cs` (если не удалены в задаче 1);
  - Infrastructure: `Persistence/Postgres/CategoryAssignmentRepository.cs`,
    `Persistence/Postgres/CommodityCategoryAssignmentEntity.cs`,
    `Persistence/Postgres/Configurations/CommodityCategoryAssignmentConfiguration.cs`,
    `DbSet<CommodityCategoryAssignmentEntity> CommodityCategoryAssignments` и вызов
    `ApplyConfiguration(...)` из `ReceiptDbContext`;
  - DI: убрать `services.AddScoped<ICategoryAssignmentRepository, CategoryAssignmentRepository>()`.
- Проверить, что `MigrationRunner` подхватывает новый скрипт автоматически.

**Затрагиваемые сервисы/файлы:** `Analytics/src/.../Migrations/Scripts/` (новый), `Persistence/Postgres/`,
`Configuration/DependencyInjectionExtensions.cs`, `Domain/Modules/Commodities/`.

**Зависимости:** задачи 1–5 (код перестал обращаться к таблице ДО применения drop-миграции).

**Приоритет:** P0
**Оценка времени:** 0.5 дня

### 7. Тесты (.NET)

**Описание:** покрытие критериев приёмки задачи (паттерн — NSubstitute + `UserContext.SetUserId`).
- Новые `InMemoryCommodityCategoryCacheTests.cs`:
  - `TryAdd`/`TryGet`; first-wins (повторный `TryAdd` того же ключа не меняет значение);
  - лимит: `MaxSize` из конфигурации; при достижении — `TryAdd` новых ключей возвращает `false`, без
    вытеснения существующих («замерзание»);
  - `Undefined` не добавляется; `MaxSize < 1` → кэш не наполняется;
  - наполнение из `commodities`: (мок/стенд) позиции с категорией ≠ `Undefined` попадают, без категории и
    с `Undefined` — нет (тест сервиса инициализации или его логики).
- `CommodityCategorizationServiceTests` (обновление): замена мока `ICategoryAssignmentRepository` →
  `ICommodityCategoryCache`; сценарии: попадание в кэш → ИИ не вызван, `source=cache` (включая «АИ-95-К5»);
  промах → ИИ вызван; приоритет «existing → cache → ai»; сохранение обновляет кэш (`TryAdd`), сброс — нет.
- `CommodityEndpointsTests` (обновление/дополнение): ручной PUT записывает в кэш (мок `ICommodityCategoryCache`),
  для `Undefined` — не записывает.
- Удалить `CategoryAssignmentRepositoryTests.cs` (таблица удалена).
- Архитектурные тесты `ProjectDependencyTests` не нарушены (контракт — Application, реализация — Infrastructure).
- Запуск: `cd Analytics && dotnet test`.

**Затрагиваемые сервисы/файлы:** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/` (новые + правки).

**Зависимости:** задачи 1–6.

**Приоритет:** P1
**Оценка времени:** 2 дня

### 8. Ручное тестирование end-to-end (критерии приёмки задачи)

**Описание:**
- Подготовка: миграции (`cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run` — применится
  drop-скрипт), запуск API + frontend, настроенный `AI:BaseUrl`.
- Проверка через curl/webUI:
  1. Наполнение при старте: в логах init-сервиса `Count`; позиции с категорией в `commodities` отдаются
     `source=cache` (или `existing` для своего чека), ИИ не вызывается.
  2. Сценарий «АИ-95-К5»: позиция без категории в новом чеке с названием, уже категоризированным в
     `commodities` (или добавленным в кэш через сохранение) → `source=cache`, ИИ не вызван (критерий приёмки
     «ручной сценарий»).
  3. Новое название → `source=ai`, без регрессий формата `{"category":"Name"}`.
  4. Ручной `PUT /api/commodities/{id}/category` → повторный suggest для такого названия в другом чеке даёт
     `source=cache`.
  5. Лимит: `CommodityCategoryCache__MaxSize=10` → после 10 записей кэш «замерзает» (новые названия → ИИ),
     вытеснения нет; изменение лимита применяется без пересборки.
  6. После drop-миграции: `\d commodity_category_assignments` в psql → таблица отсутствует; код не обращается
     к ней (отсутствие ошибок в логах).
  7. `cd Analytics && dotnet test` — все тесты проходят.
- Регрессия: порт `5039`, webUI «Категоризировать»/«Сохранить», список всех позиций с фильтрами — без изменений.

**Затрагиваемые сервисы/файлы:** окружение (PostgreSQL, Analytics + frontend), конфигурация.

**Зависимости:** задачи 6, 7.

**Приоритет:** P1
**Оценка времени:** 1 день

## План выполнения

1. Задачи 1–2 (контракт/реализация кэша + конфигурация/DI) — параллельно.
2. Задача 3 (наполнение при старте).
3. Задача 4 (подключение кэша в сервис категоризации, фикс FR-2.2).
4. Задача 5 (ручной PUT → кэш) и задача 6 (удаление таблицы/EF-кода) — параллельно после 4.
5. Задача 7 (тесты).
6. Задача 8 (ручное E2E, включая «АИ-95-К5»).

## Критический путь

1. Задачи 1–4 (кэш + наполнение + подключение) — основа.
2. Задача 6 (удаление таблицы) — только после того, как код перестанет обращаться к таблице.
3. Задачи 7–8 (тесты + приёмка).

## Критерии приёмки (из задачи `docs/tasks/in-memory-commodity-category-cache.md`)

- [ ] Автотест: при старте сервиса кэш наполняется из `commodities` (≠ `Undefined` попадают, без категории и
      `Undefined` — нет).
- [ ] Автотест: совпадение с кэшем → ИИ не вызывается, значение из кэша (сценарий «АИ-95-К5»).
- [ ] Автотест: отсутствие совпадения → ИИ вызывается (регрессия).
- [ ] Автотест: лимит (по умолчанию 1000) — кэш перестаёт обновляться, ничего не вытесняется.
- [ ] Автотест: лимит настраивается через конфигурацию/переменную окружения без пересборки.
- [ ] Миграция удаления таблицы применяется без ошибок; код больше не обращается к таблице.
- [ ] Ручной сценарий: категоризация чека с «АИ-95-К5» в webUI не вызывает ИИ при наличии значения в кэше.
- [ ] `cd Analytics && dotnet test` — все тесты проходят.

## Последовательность миграции (развёртывание)

1. **Схема данных:** новая версия кода Analytics **не читает и не пишет** таблицу `commodity_category_assignments`
   (задачи 1–5) → единый релиз безопасен.
2. **Единый релиз (рекомендуется):**
   - деплой нового бинарника Analytics (кэш in-memory, наполнение при старте);
   - запуск проекта миграций — применяется `20260922120000_drop_commodity_category_assignments.sql`
     (`DROP TABLE IF EXISTS`); миграции выполняются до старта API;
   - проверка: таблица отсутствует, `suggest` работает без ошибок, кэш наполнен.
3. **Откат (если потребуется):** старый бинарник обращается к таблице → перед откатом восстановить таблицу
   из резервной копии (`pg_dump -t commodity_category_assignments` перед деплоем) и затем откатить код.
   При строгих требованиях к откату — двухфазный деплой: сначала код (таблица остаётся), затем drop-миграция.
4. **Бэкап:** `./backup.sh` покрывает **только MongoDB** (базы `receipt_collection`, `receipt-data`) — таблица
   `commodity_category_assignments` в него **не попадает** (она в PostgreSQL). Для отката достаточно точечного
   дампа таблицы (см. п. 3); при желании полного бэкапа схемы — `docker exec receipt-postgres pg_dump -d receipts`
   (имя сервиса/БД — по `docker-compose.yml`, `PG_LOGIN`/`PG_SECRET` из `.env`). После удаления таблицы бэкапы
   её не содержат (история не требуется — источник истины `commodities`).

## Открытые вопросы (требуют подтверждения)

1. **Сброс категории и устаревший кэш:** сброс (`category = null`) не удаляет запись кэша — до рестарта
   suggest может предлагать устаревшую категорию для такого названия. Принято как следствие first-wins/«замерзания»
   (ADR 019, «Отрицательные последствия»); вариант «удалять запись при сбросе» требует стратегии разрешения
   конфликтов и относится к бэклогу задачи.
2. **`suggest` при пустом `AI:BaseUrl`** по-прежнему отвечает `503` (контракт ADR 009 не меняется), хотя часть
   позиций могла бы обслуживаться кэшем. Изменение поведения — в бэклог.
3. **`MaxSize < 1`:** трактуется как «кэш отключён» (ИИ вызывается всегда). Подтверждается при ревью задачи 2.
4. **Падение init-сервиса при недоступной БД на старте** — fail-fast (сервис не поднимается), по аналогии с
   `ReceiptSynchronizationHostedService`. Подтверждается при ревью задачи 3.
5. **Метрика «кэш заморожен»:** при достижении лимита желателен лог/warning (реализуется в задаче 1/2; формат —
   на усмотрение разработчика).