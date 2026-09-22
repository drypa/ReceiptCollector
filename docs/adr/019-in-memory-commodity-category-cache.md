# ADR-019: In-memory кэш категорий товаров (замена `commodity_category_assignments`)

## Статус

Принято (проект). Уточняет/заменяет часть решений [ADR 009](009-auto-commodity-categorization.md):
решение **C1/C4** (таблица PostgreSQL `commodity_category_assignments` как сквозной кэш ранее присвоенных
категорий), а также связанные с ними пункты деталей решения (доменная сущность `CommodityCategoryAssignment`,
контракт `ICategoryAssignmentRepository`, репозиторий `CategoryAssignmentRepository`, миграция
`20260919120000_add_commodity_category_assignments.sql`). Остальные решения ADR 009 (алгоритм приоритетов
«existing → cache → ai» и шаги 1/3, эндпоинты, конфигурация `AI:*`, webUI и т.д.) **не изменяются**.

Основание — задача [in-memory-commodity-category-cache](../tasks/in-memory-commodity-category-cache.md)
(требования FR-1…FR-2, критерии приёмки).

## Контекст

### Проблема

- При категоризации чека в webUI система вызывает ИИ для названий товаров, для которых категория **уже известна**
  (наблюдение заказчика на примере «АИ-95-К5»: запись в таблице `commodity_category_assignments` есть, а запрос
  к ИИ всё равно уходит).
- Кэш хранится в PostgreSQL (`commodity_category_assignments`): накопились «дубликаты», непонятно, какую запись
  обновлять; каждая запись при сохранении — лишняя DB-транзакция; каждый suggest делает DB-запрос на позицию.
- Каждый вызов ИИ — прямые денежные затраты; повторные вызовы для одинаковых названий неоправданны.

### Требования (из задачи)

- **FR-1.1** — in-memory структура ключ-значение: ключ — нормализованное название товара, значение —
  категория `CommodityCategory`.
- **FR-1.2** — наполнение при старте из `commodities` (категория заполнена и ≠ `Undefined`).
- **FR-1.3** — обновление при изменении категории: и при ручном `PUT /api/commodities/{id}/category`,
  и при сохранении после категоризации.
- **FR-1.4** — дубликаты допустимы: при изменении категории для названия, уже присутствующего в кэше,
  старое значение остаётся (разрешение конфликтов — в бэклог).
- **FR-1.5** — лимит размера (по умолчанию 1000, конфигурируемый); при достижении лимита кэш «замерзает»
  (без вытеснения).
- **FR-1.6** — таблица `commodity_category_assignments` удаляется миграцией; код больше не обращается к ней.
- **FR-1.7** — источник истины для наполнения кэша — таблица `commodities`.
- **FR-2.1…FR-2.4** — приоритеты «существующая категория позиции → кэш → ИИ»; при совпадении с кэшем ИИ
  не вызывается (`source = cache`); масштаб кэша — сквозной для всех пользователей (без `user_id`).

### Анализ причины «запись в кэше есть, а ИИ всё равно вызывается»

Проверка кода (`Analytics/src`) выявила три подтверждённые причины:

1. **Ручной `PUT /api/commodities/{id}/category` не записывал в кэш** (`CommodityEndpoints.UpdateCategory`
   вызывает только `ICommodityRepository.UpdateCategoryAsync`). Запись в `commodity_category_assignments`
   выполнялась только из `CommodityCategorizationService.ApplyConfirmedCategoriesAsync` (подтверждение из webUI).
   Категории, проставленные вручную на странице «Товары», в кэш не попадали → для таких названий в других чеках
   всегда был промах кэша → вызов ИИ. Это прямое нарушение FR-4.2 исходной задачи.
2. **Недостаточная нормализация ключа**: `CommodityNameNormalizer` выполняет только lowercase + trim +
   схлопывание пробелов (правило подтверждено в задаче, FR-2.3). Визуально одинаковые названия с разными
   символами (кириллица/латиница: «А» vs «A», «И» vs «I»; дефис/минус/тире: `-`, `‑`, `–`, `—`; `ё` vs `е`;
   неразрывный пробел) дают **разные** `normalized_name` → точечный поиск по уникальному индексу
   `FirstOrDefault(a => a.NormalizedName == normalized)` даёт промах → ИИ. Этим же объясняются «дубликаты» в
   таблице при уникальном индексе: это семантические дубликаты (разные варианты написания одного названия),
   а не точные дубли строк.
3. **Асимметрия маппинга «категория задана»**: `ReceiptEntity.MapToDomain()` возвращает `Category = null`,
   если задан только `category_id`, а `category_name` отсутствует (исторические данные). Такая позиция
   трактуется как «без категории» (шаг 1 алгоритма пропускается) и при отсутствии точного ключа в кэше
   уходит в ИИ.

Дополнительно возможен операционный фактор (запись появилась в таблице уже после последнего запуска suggest),
но кодовые причины 1–3 достаточны, чтобы объяснить наблюдаемое поведение.

**Как новая схема устраняет причины:**
- наполнение кэша при старте из `commodities` (причина 3) — позиция с проставленной категорией становится
  частью кэша независимо от `category_name`; дисплейное имя берётся из `CommodityCategoryHelper`, а не из БД;
- ручной PUT теперь пишет в кэш (причина 1);
- причина 2 полностью не устраняется (правило нормализации подтверждено в задаче) — расширение нормализации
  (гомоглифы, дефисы, ё/е) вынесено в бэклог задачи; для «АИ-95-К5» с точным совпадением строки после
  lowercase/trim кэш сработает.

## Рассмотренные варианты

### A. Хранилище кэша

#### A1: In-memory кэш в процессе Analytics — **выбран**

**Описание:** Singleton-сервис в Analytics, `ConcurrentDictionary<string, CommodityCategory>` (ключ —
нормализованное название), наполняется при старте из `commodities` и обновляется при подтверждении/ручном
назначении категорий. Лимит размера, «замерзание» при достижении лимита, без вытеснения.

**Плюсы:**
- Мгновенный доступ без сетевого/DB-раундтрипа в suggest (сейчас каждый промах шага 1 делает
  `FirstOrDefaultAsync` по таблице на каждую позицию).
- Ноль дубликатов по ключу, нет транзакций на запись кэша (FR-1.4: first-wins).
- Наполнение из `commodities` (FR-1.7) автоматически «чинит» историческую асимметрию `category_id` без
  `category_name` (причина 3).
- Нет новой инфраструктуры; Analytics — единственный сервис, работающий с категориями (ADR 009, решение A1).
- Перезапуск кэша при рестарте — гарантированное восстановление согласованности с источником истины (в отличие
  от таблицы, которая могла «устаревать» при непройденных путях записи, причина 1).

**Минусы:**
- Потеря при рестарте (кэш недолговечен по определению) — компенсируется наполнением при старте; это и есть
  требуемое поведение (edge case «Рестарт сервиса» задачи).
- При нескольких экземплярах Analytics кэши рассинхронизируются — в проде сервис разворачивается одним
  инстансом (см. «Риски»); вопрос масштабирования вынесен в бэклог (при появлении multi-instance —
  переход на Redis/распределённый кэш).
- Лимит 1000 при большом справочнике именований — «замерзание» (принимается; FR-1.5, значение настраивается).

#### A2: Таблица PostgreSQL `commodity_category_assignments` (текущее решение C1/C4 ADR 009) — **заменяется**

**Почему заменяется:**
- Несколько путей записи (ручной PUT не писал в таблицу) породили неоднозначность и «дубликаты» — непонятно,
  какую запись обновлять (причина 1 выше).
- DB-запрос на каждую позицию в suggest (латентность) и DB-запись на каждый подтверждённый товар (транзакция) —
  в то время как кэш должен быть самым быстрым звеном.
- Проблема согласованности «таблица vs commodities»: при сбросе категории запись в таблице не удалялась,
  при изменении категории через разные пути — расходились.
- Задача заказчика прямо требует in-memory кэш и удаление таблицы.

#### A3: Внешний кэш (Redis) — **отклонён**

**Минусы:** новая инфраструктура и развёртывание ради одного инстанса сервиса; противоречит KISS/YAGNI и
направлению задачи (in-memory). Зафиксирован путь эволюции на A3 при появлении multi-instance Analytics.

### B. Семантика записи при конфликте ключа

#### B1: First-wins (первое значение выигрывает) — **выбран**

**Описание:** `TryAdd` не перезаписывает существующее значение ключа; «дубликаты» (изменение категории для уже
присутствующего названия) трактуются как «старое значение остаётся» (FR-1.4, edge case «Конфликт значений»:
берётся первое подходящее значение).

**Плюсы:** соответствует FR-1.4 и edge case задачи дословно; просто, детерминированно; разрешение конфликтов
(например, last-wins) остаётся в бэклоге как отдельная осознанная задача.

**Минусы:** повторная ручная перекатегоризация названия, уже попавшего в кэш, не влияет на последующие
предложения — принято в рамках задачи (бэклог).

#### B2: Last-wins — **отклонён на данном этапе**

**Плюсы:** «свежая» категория отражается в предложениях.

**Минусы:** противоречит тексту FR-1.4 («устаревшее значение остаётся») и edge case («первое подходящее
значение»); потребовал бы стратегии разрешения конфликтов и аудита исторических данных — это и есть отдельная
задача в бэклоге исходной постановки.

### C. Место контракта и реализации кэша (слои)

#### C1: Контракт в Application, реализация в Infrastructure — **выбран**

**Описание:** `ICommodityCategoryCache` — в `Application/Modules/Commodities/Contracts/` (как `IAiClient`),
реализация `InMemoryCommodityCategoryCache` — в `Infrastructure/Modules/Commodities/`, регистрация `Singleton`.

**Плюсы:** кэш — прикладной, а не доменный, концепт; слои не нарушаются (`ProjectDependencyTests`:
Infrastructure → Application + Domain); симметрично с существующими контрактами категоризации
(`IAiClient`, `ICommodityCategorizationService` — Application).

**Минусы:** миграция контракта из Domain (где был `ICategoryAssignmentRepository`) — небольшой diff в тестах
(принято).

#### C2: Контракт остаётся в Domain — отклонён

Формально допустимо (как было у `ICategoryAssignmentRepository`), но кэш не является доменной сущностью;
плюсов по сравнению с C1 нет.

## Решение

### 1. Контракт `ICommodityCategoryCache` (Application)

Новый файл `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategoryCache.cs`:

```csharp
public interface ICommodityCategoryCache
{
    /// <summary>Текущее число записей в кэше.</summary>
    int Count { get; }

    /// <summary>Поиск категории по нормализованному названию (FR-2.2, шаг 2 алгоритма).</summary>
    bool TryGet(string normalizedName, out CommodityCategory category);

    /// <summary>
    /// Добавление записи. First-wins: если ключ уже есть — no-op (FR-1.4).
    /// При Count ≥ MaxSize (лимит достигнут) — no-op: кэш «замерзает», вытеснения нет (FR-1.5).
    /// Undefined не добавляется (FR-1.2).
    /// Возвращает true, если запись добавлена.
    /// </summary>
    bool TryAdd(string normalizedName, CommodityCategory category);
}
```

Удаляются из Domain: `CommodityCategoryAssignment.cs`, `ICategoryAssignmentRepository.cs`
(заменены контрактом выше). `CommodityNameNormalizer.cs` **остаётся** в Domain (используется и кэшем,
и сервисом категоризации).

### 2. Реализация `InMemoryCommodityCategoryCache` (Infrastructure)

Новый файл `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/InMemoryCommodityCategoryCache.cs`:

- `internal sealed class`, конструктор принимает `int maxSize` (из конфигурации);
- хранилище — `ConcurrentDictionary<string, CommodityCategory>` (упрощает параллельные чтения);
- `TryAdd`: блокировка по монитору (или `SemaphoreSlim`) вокруг проверки «ключ уже есть → прежнее значение;
  `Count >= MaxSize` → «заморозка»; иначе добавить». Атомарность нужна только для проверки лимита и гонки
  «два одинаковых ключа», сами операции словаря потокобезопасны;
- `TryGet` — синхронный `out`-вызов без аллокаций и async-обвязки (кэш — горячий путь suggest);
- семантика записи — **first-wins** (B1): повторное `TryAdd` с тем же ключом не изменяет значение;
- категория `Undefined` не добавляется (при вызове из кода ей предшествует проверка, но реализация
  дополнительно защищается `ArgumentOutOfRangeException`/no-op для `Undefined` — единая точка защиты).

Удаляются из Infrastructure: `Persistence/Postgres/CategoryAssignmentRepository.cs`,
`Persistence/Postgres/CommodityCategoryAssignmentEntity.cs`,
`Persistence/Postgres/Configurations/CommodityCategoryAssignmentConfiguration.cs`, свойство
`DbSet<CommodityCategoryAssignmentEntity> CommodityCategoryAssignments` и вызов
`ApplyConfiguration(new CommodityCategoryAssignmentConfiguration())` из `ReceiptDbContext`.

### 3. Конфигурация лимита (NFR-1 стиль)

Новый файл `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/CommodityCategoryCacheOptions.cs`:

```csharp
public sealed class CommodityCategoryCacheOptions
{
    public const string SectionName = "CommodityCategoryCache";
    public int MaxSize { get; set; } = 1000;
}
```

- `appsettings*.json`: секция `"CommodityCategoryCache": { "MaxSize": 1000 }`;
- env: `CommodityCategoryCache__MaxSize` — без пересборки (стиль `AI__*`);
- биндинг: `services.AddOptions<CommodityCategoryCacheOptions>().Bind(configuration.GetSection(...))`
  в `DependencyInjectionExtensions.ConfigureInfrastructureOptions`;
- регистрация: `services.AddSingleton<ICommodityCategoryCache>(sp => new InMemoryCommodityCategoryCache(
  sp.GetRequiredService<IOptions<CommodityCategoryCacheOptions>>().Value.MaxSize));`
  (опция считается при старте; изменение требует рестарта — приемлемо, это параметр объёма памяти);
- защита от невалидного `MaxSize ≤ 0` → реализация трактует как «кэш отключён»/значение по умолчанию
  (на усмотрение реализации; в документации фиксируем: `MaxSize < 1` ⇒ кэш не наполняется).

### 4. Наполнение при старте (FR-1.2, FR-1.7)

Новый `HostedService` `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/CommodityCategoryCacheInitializationHostedService.cs`
(паттерн `ReceiptSynchronizationHostedService`: `IServiceScopeFactory` → scoped `ReceiptDbContext`):

1. В `StartAsync` создать scope, получить `ReceiptDbContext`.
2. Выбрать позиции: `Commodities.AsNoTracking().Where(c => c.CategoryId != null && c.CategoryId != 0)`
   (категория заполнена и ≠ `Undefined`; FR-1.2), **проекция только нужных колонок**
   `Select(c => new { c.Name, c.CategoryId })`, `OrderBy(c => c.Name)` (детерминированный порядок для first-wins
   при равных названиях).
   Примечание о query filter: глобальный filter `ReceiptDbContext` (`r.UserId == CurrentUserId`) настроен **только
   на `ReceiptEntity`**, прямое чтение `DbSet<CommodityEntity>` его не задействует — запрос охватывает позиции всех
   пользователей, что **соответствует сквозному масштабу кэша** (FR-2.4). Добавлять фильтр по `userId` в запрос
   наполнения **нельзя**.
3. Для каждой позиции: пропустить `CategoryId`, не являющийся валидным значением `CommodityCategory`
   (`Enum.IsDefined`); ключ = `CommodityNameNormalizer.NormalizeName(Name)`;
   `TryAdd(key, (CommodityCategory)CategoryId)`.
4. **Раннее завершение перебора**: как только `Count == MaxSize` (лимит достигнут) — прервать перебор (`break`).
   При first-wins и `OrderBy(Name)` дальнейшие `TryAdd` всё равно были бы no-op (FR-1.5 «замерзание»), поэтому
   ранний выход даёт тот же результат и экономит чтение/нормализацию при большом справочнике.
5. Залогировать `Count` после наполнения; ошибка подключения/запроса — `throw` (аналогично
   `ReceiptSynchronizationHostedService`, который не глотает ошибки старта; без БД сервис бесполезен).
6. `StopAsync` — `Task.CompletedTask`.

Порядок запуска hosted-сервисов в ASP.NET Core: `StartAsync` всех `IHostedService` завершается до приёма
HTTP-запросов — кэш будет наполнен до первого `suggest` (окно недоступности кэша отсутствует).

Примечание: наполнение не зависит от миграций — в рамках одной деплой-процедуры миграции (включая удаление
таблицы) выполняются раньше старта API.

### 5. Использование кэша в алгоритме категоризации (FR-2.1…FR-2.4)

`CommodityCategorizationService` (Infrastructure):

- зависимость `ICategoryAssignmentRepository` заменяется на `ICommodityCategoryCache`;
- **шаг 2** в `SuggestForReceiptAsync`: `if (_cache.TryGet(CommodityNameNormalizer.NormalizeName(item.Name), out var category))`
  → `source = cache`, ИИ **не вызывается** (FR-2.2). Шаги 1 и 3 не меняются; дедупликация AI-вызовов по
  нормализованному имени сохраняется;
- **сохранение** в `ApplyConfirmedCategoriesAsync`: после `UpdateCategoryAsync` для каждой позиции с назначенной
  категорией — `_cache.TryAdd(normalized, category)` (FR-1.3). Сброс (`null`) в кэш не пишется (и не удаляется —
  см. «Отрицательные последствия»).

### 6. Ручное назначение категории `PUT /api/commodities/{id}/category` (FR-1.3, причина 1)

`CommodityEndpoints.UpdateCategory` (Api):

- добавить параметр `[FromServices] ICommodityCategoryCache cache`;
- после `commodityRepository.UpdateCategoryAsync(id, category, ct)` — 
  `cache.TryAdd(CommodityNameNormalizer.NormalizeName(commodity.Name), category)` для `category != CommodityCategory.Undefined`
  (`commodity` уже получен из репозитория выше; дополнительный запрос не требуется);
- это устраняет пробел исходной реализации («ручная категоризация не попадала в кэш»).

### 7. Удаление таблицы (FR-1.6)

Новый SQL-скрипт `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20260922120000_drop_commodity_category_assignments.sql`:

```sql
DROP TABLE IF EXISTS commodity_category_assignments;
```

- `IF EXISTS` — среда, где миграция `20260919120000_add_commodity_category_assignments.sql` ещё не применялась,
  не сломается;
- данные таблицы **не переносятся** (FR-1.6/FR-1.7): источник истины — `commodities`, «дубликаты» из таблицы
  не актуальны, кэш наполняется при старте из `commodities`;
- после миграции новая версия кода не содержит ни одного обращения к таблице (см. п. 2 — удалён EF-код;
  п. 1 — удалён доменный контракт).

### 8. Реестр изменений по слоям (карта для плана)

| Слой / файл | Действие |
|-------------|----------|
| `Domain/.../CommodityCategoryAssignment.cs` | удалить |
| `Domain/.../ICategoryAssignmentRepository.cs` | удалить |
| `Domain/.../CommodityNameNormalizer.cs` | без изменений |
| `Application/.../Contracts/ICommodityCategoryCache.cs` | новый контракт |
| `Infrastructure/Modules/Commodities/InMemoryCommodityCategoryCache.cs` | новая реализация |
| `Infrastructure/Configuration/Options/CommodityCategoryCacheOptions.cs` | новая опция |
| `Infrastructure/Synchronization/CommodityCategoryCacheInitializationHostedService.cs` | новый hosted service |
| `Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` | шаг 2 + сохранение через cache |
| `Infrastructure/Persistence/Postgres/CategoryAssignmentRepository.cs` | удалить |
| `Infrastructure/Persistence/Postgres/CommodityCategoryAssignmentEntity.cs` | удалить |
| `Infrastructure/Persistence/Postgres/Configurations/CommodityCategoryAssignmentConfiguration.cs` | удалить |
| `Infrastructure/Persistence/Postgres/ReceiptDbContext.cs` | удалить DbSet + ApplyConfiguration |
| `Infrastructure/Configuration/DependencyInjectionExtensions.cs` | DI: singleton cache + hosted service + options |
| `Api/Modules/Commodities/CommodityEndpoints.cs` | обновление кэша в `PUT /api/commodities/{id}/category` |
| `Migrations/Scripts/20260922120000_drop_commodity_category_assignments.sql` | новый скрипт (DROP TABLE IF EXISTS) |
| `Analytics/frontend` | **без изменений** (контракты API не меняются) |
| Backend (Go), Telegram-бот, nginx | **без изменений** |

## Последствия

### Положительные

- Сокращение вызовов ИИ для известных названий: попадание в кэш обслуживается без ИИ и без DB-запроса
  (быстрее и дешевле, чем текущая схема с таблицей).
- Устранена причина «запись есть, а ИИ вызывается» для ручных категорий (п. 6) и для позиций с проставленной
  категорией в `commodities` (наполнение при старте).
- Ноль дубликатов по ключу (структура ключ-значение, first-wins), нет транзакций на запись кэша.
- Удаляется таблица и связанный EF/доменный код — меньше поверхность для рассинхронизации.
- Единая точка записи кэша (два явных места: подтверждение из webUI и ручной PUT), наполнение из источника
  истины `commodities` при старте — модель проще и предсказуемее.
- Конфигурация лимита через env без пересборки (стиль `AI__*`), поведение «замерзания» предсказуемо и
  тестируемо.

### Отрицательные

- Кэш недолговечен: после рестарта наполняется заново (принимается задачей; наполнение — при старте).
- Сброс категории (`null`) не удаляет запись из кэша: последующие suggest могут предлагать устаревшую категорию
  для позиций с таким названием до рестарта. Осознанное следствие first-wins и «замерзания»; митигация —
  рестарт/наполнение, разрешение конфликтов — бэклог задачи.
- При достижении лимита (1000 по умолчанию) новые названия всегда уходят в ИИ — функционал не ломается,
  но кэш перестаёт наполняться до повышения `MaxSize` или рестарта.

### Компромиссы

- **In-memory вместо таблицы/Redis** — максимальная скорость и простота против недолговечности и
  одноинстансности (в проде Analytics — один экземпляр; путь эволюции на Redis зафиксирован).
- **First-wins вместо last-wins** — соответствие FR-1.4 и детерминизм против «свежести» категорий
  (разрешение конфликтов — бэклог).
- **`suggest` при пустом `AI:BaseUrl` по-прежнему отвечает `503`** (контракт ADR 009 не меняется), хотя теперь
  часть позиций могла бы обслуживаться кэшем. Сознательно оставлено для стабильности контракта; изменение
  поведения (per-item обработка без ИИ) — в бэклог.
- **Наполнение только при старте** (без онлайн-пересканирования) — простота против «свежести» после ручных
  правок вне двух точек записи; все точки записи категорий контролируются кодом, поэтому расхождение
  ограничено сбросами категорий (см. «Отрицательные»).

### Риски

- **Рестарт/деплой**: между стартом и наполнением кэша окно отсутствует (hosted service завершает
  `StartAsync` до приёма запросов). При недоступности БД на старте — сервис не поднимется (fail-fast,
  как у синхронизации). **Митигация:** проверка подключения в init-сервисе; ошибки логируются.
- **Лимит 1000 исчерпан** (справочник товаров может превысить): новые названия — всегда ИИ. **Митигация:**
  `CommodityCategoryCache__MaxSize` повышается через env без пересборки; желательно добавить метрику/лог
  «cache frozen» при достижении лимита.
- **Multi-instance в будущем**: рассинхронизация кэшей. **Митигация:** сейчас один инстанс; при
  горизонтальном масштабировании — переход на Redis (A3), зафиксировано как путь эволюции.
- **Откат релиза после удаления таблицы**: старая версия кода обращается к таблице → ошибки при suggest.
  **Митигация:** перед деплоем — резервный dump таблицы (`pg_dump -t commodity_category_assignments`);
  при плановом откате — восстановить таблицу из dump и откатить бинарник (см. «Последовательность миграции» в
  плане).
- **Гонки записи кэша** (параллельные suggest/save): операции словаря потокобезопасны; проверка лимита — под
  атомарной блокировкой; худший случай — незначительное превышение лимита в момент гонки (приемлемо,
  документируется в реализации как допущение без вытеснения).
- **`MapToDomain` остаётся асимметричным** (`category_name` обязателен для доменной категории) — на шаг 1 это
  влияет, но теперь провал шага 1 накрывается кэшем, наполненным по `category_id` из `commodities`.
- **Расширение нормализации не выполняется** (гомоглифы/дефисы/ё) — часть промахов останется; риск осознанный,
  в бэклоге задачи.

## Связь с ADR 009

- ADR 009: решение **C1/C4** и связанные пункты деталей (доменная сущность, контракт, репозиторий, миграция
  `20260919120000_add_commodity_category_assignments.sql`) **заменяются** настоящим документом.
- Алгоритм приоритетов «existing → cache → ai» (шаги 1–3), эндпоинты `POST /api/receipts/{id}/categories/suggest`
  и `PUT /api/receipts/{id}/categories`, конфигурация `AI:*`, решения A1/B1/D1/E1/F1/G1/H1/H2/I1 ADR 009 —
  **без изменений**.
- Требование FR-4.2/UC-5 исходной задачи (кэш в `commodity_category_assignments`) уточнено новой задачей
  заказчика.

## Ссылки

- [Задача: In-memory кэш категорий товаров](../tasks/in-memory-commodity-category-cache.md)
- [Задача (завершённая): Автоматическая категоризация товаров](../tasks/completed/auto-commodity-categorization.md)
- [ADR 009: Автоматическая категоризация товаров в чеке](009-auto-commodity-categorization.md)
- [ADR 010: Расширение справочника категорий товаров](010-commodity-categories-expansion.md)
- [План: In-memory кэш категорий товаров](../plans/in-memory-commodity-category-cache.md)
- Фактический код: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategorizationService.cs`,
  `Api/Modules/Commodities/CommodityEndpoints.cs`, `Persistence/Postgres/ReceiptDbContext.cs`,
  `Configuration/DependencyInjectionExtensions.cs`, `Migrations/.../20260919120000_add_commodity_category_assignments.sql`