# Детализированные шаги: In-memory кэш категорий товаров

> Пошаговая инструкция для разработчика. Верхнеуровневый план с приоритетами и оценками —
> [in-memory-commodity-category-cache.md](in-memory-commodity-category-cache.md); архитектурные решения —
> [ADR 019](../adr/019-in-memory-commodity-category-cache.md). Новых архитектурных решений шаги не содержат —
> все детали ниже жёстко следуют ADR 019. Все работы — только в репозитории **Analytics (.NET)**.

## 0. Инварианты реализации (из ADR 019 — обязательны, не обсуждаются)

1. **First-wins** (FR-1.4, решение B1): `TryAdd` не перезаписывает существующий ключ; повторный `TryAdd` — `false`/no-op.
2. **Лимит** (FR-1.5): `MaxSize` по умолчанию **1000**; при `Count >= MaxSize` кэш «замерзает» — `TryAdd` новых ключей возвращает `false`, **вытеснения нет**.
3. **Ранний выход при наполнении** (ADR 019 п.4): перебор позиций `commodities` прерывается (`break`), как только `Count == MaxSize`.
4. **Проекция при наполнении** (ADR 019 п.4): только `Select(c => new { c.Name, c.CategoryId })`, без полных сущностей и навигаций.
5. **Query filter**: глобальный фильтр `ReceiptDbContext` настроен только на `ReceiptEntity`; запрос наполнения через `DbSet<CommodityEntity>` охватывает позиции **всех** пользователей (сквозной кэш, FR-2.4). **Фильтр по `userId` в запрос НЕ добавлять.**
6. **Дисплейное имя** берётся из `CommodityCategoryHelper.GetDisplayName()` (а не из БД) — и в шаге кэша алгоритма, и в реализациях.
7. **`MaxSize < 1`** ⇒ «кэш отключён»: `TryAdd` всегда `false`, наполнение не выполняется, ИИ вызывается всегда (документированный edge case; подтверждён в плане, «Открытые вопросы» №3).
8. **`Undefined` не попадает в кэш** (FR-1.2): защита на уровне реализации `TryAdd` + фильтр запроса наполнения `CategoryId != 0`.
9. **Вне области задачи** (не трогать): `Analytics/frontend`, Backend (Go), Telegram-бот, nginx, порт `5039`, MongoDB (read-only), проект Migrations кроме нового SQL-скрипта.

## 1. Карта изменений по слоям (из ADR 019 п.8 — к чему приводим)

| Слой / файл | Действие |
|---|---|
| `Application/Modules/Commodities/Contracts/ICommodityCategoryCache.cs` | **новый** контракт (шаг 1.1) |
| `Infrastructure/Modules/Commodities/InMemoryCommodityCategoryCache.cs` | **новая** реализация (шаг 1.2) |
| `Infrastructure/Configuration/Options/CommodityCategoryCacheOptions.cs` | **новая** опция (шаг 2.1) |
| `Api/appsettings.json`, `Api/appsettings.Development.json` | секция `CommodityCategoryCache:MaxSize` (шаг 2.2) |
| `Infrastructure/Configuration/DependencyInjectionExtensions.cs` | биндинг опций + singleton кэша + hosted service; **убрать** регистрацию `ICategoryAssignmentRepository` (шаги 2.3, 3.2, 6.2) |
| `Infrastructure/Synchronization/CommodityCategoryCacheInitializationHostedService.cs` | **новый** hosted service (шаг 3.1) |
| `Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` | шаг 2 через кэш + `TryAdd` при сохранении (шаги 4.1, 4.2) |
| `Api/Modules/Commodities/CommodityEndpoints.cs` | обновление кэша в `PUT /api/commodities/{id}/category` (шаг 5.1) |
| `Migrations/Scripts/20260922120000_drop_commodity_category_assignments.sql` | **новый** скрипт `DROP TABLE IF EXISTS` (шаг 6.1) |
| `Domain/.../CommodityCategoryAssignment.cs`, `ICategoryAssignmentRepository.cs` | **удалить** (шаг 6.2) |
| `Infrastructure/Persistence/Postgres/CategoryAssignmentRepository.cs`, `CommodityCategoryAssignmentEntity.cs`, `Configurations/CommodityCategoryAssignmentConfiguration.cs` | **удалить** (шаг 6.2) |
| `Infrastructure/Persistence/Postgres/ReceiptDbContext.cs` | убрать `DbSet` + `ApplyConfiguration` (шаг 6.2) |
| `Domain/.../CommodityNameNormalizer.cs` | **без изменений** |

---

## Задача 1. Контракт и реализация кэша (Application + Infrastructure)

### Шаг 1.1 — Контракт `ICommodityCategoryCache` (Application)

**Файлы:** создать `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategoryCache.cs`.

**Объём изменений:** файл с интерфейсом (текст — дословно по ADR 019 п.1):

```csharp
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// In-memory кэш категорий товаров (ADR 019). Ключ — нормализованное название товара,
/// значение — категория CommodityCategory. Масштаб — сквозной для всех пользователей (без userId, FR-2.4).
/// Реализация — Infrastructure (InMemoryCommodityCategoryCache), регистрация Singleton.
/// </summary>
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

Почему сюда: контракт — прикладной, а не доменный концепт (ADR 019, решение C1); `Application` зависит от `Domain` (тип `CommodityCategory`), слои не нарушаются (проверяется `ProjectDependencyTests`).

**Правки тестов:** нет.

**Критерий готовности:** `cd Analytics && dotnet build` — зелёная сборка (проект Application). Новый файл компилируется.

### Шаг 1.2 — Реализация `InMemoryCommodityCategoryCache` (Infrastructure)

**Файлы:** создать `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/InMemoryCommodityCategoryCache.cs`.

**Объём изменений** (ADR 019, п.2 — все требования):

- `internal sealed class InMemoryCommodityCategoryCache : ICommodityCategoryCache`;
- конструктор `InMemoryCommodityCategoryCache(int maxSize)` — значение из конфигурации (шаг 2.1/2.3);
- хранилище `ConcurrentDictionary<string, CommodityCategory>` (чтения без блокировок);
- `Count => _items.Count`;
- `TryGet` — синхронный `out`-вызов `_items.TryGetValue(normalizedName, out category)`, без async/без аллокаций (кэш — горячий путь suggest);
- `TryAdd` — под `lock (_gate)`: first-wins (`ContainsKey` → `false`, значение не меняется), «заморозка» (`_items.Count >= _maxSize` → `false`, без вытеснения), иначе `_items.TryAdd`. **До блокировки**: `_maxSize < 1` → `false` (кэш отключён, инвариант 7); `category == CommodityCategory.Undefined` → `false` (единая точка защиты от `Undefined`, инвариант 8);
- «Допущение о гонке» (ADR 019, «Риски»): худший случай при параллельных `TryAdd` — незначительное превышение лимита; фиксируется комментарием в коде, вытеснения нет.

```csharp
using System.Collections.Concurrent;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

internal sealed class InMemoryCommodityCategoryCache : ICommodityCategoryCache
{
    private readonly ConcurrentDictionary<string, CommodityCategory> _items = new();
    private readonly int _maxSize;
    private readonly object _gate = new();

    public InMemoryCommodityCategoryCache(int maxSize)
    {
        _maxSize = maxSize;
    }

    public int Count => _items.Count;

    public bool TryGet(string normalizedName, out CommodityCategory category)
        => _items.TryGetValue(normalizedName, out category);

    public bool TryAdd(string normalizedName, CommodityCategory category)
    {
        // MaxSize < 1 — кэш отключён; Undefined не хранится (FR-1.2).
        if (_maxSize < 1 || category == CommodityCategory.Undefined)
        {
            return false;
        }

        // LOCK: атомарность нужна только для проверки лимита и гонки «два одинаковых ключа».
        // Допущение: при гонке возможна незначительная переоценка лимита — без вытеснения (ADR 019, «Риски»).
        lock (_gate)
        {
            if (_items.ContainsKey(normalizedName) || _items.Count >= _maxSize)
            {
                return false; // first-wins (FR-1.4) / «заморозка» (FR-1.5)
            }

            return _items.TryAdd(normalizedName, category);
        }
    }
}
```

**Правки тестов:** нет (тесты кэша — задача 7, шаг 7.1).

**Критерий готовности:** `cd Analytics && dotnet build` — зелёная сборка Infrastructure. Поведенческие проверки — шаг 7.1 (в этом шаге допускается ручная проверка через временный вызов, если нужно).

> **Примечание по задаче 1 плана** (удаление доменных файлов `CommodityCategoryAssignment.cs` / `ICategoryAssignmentRepository.cs`): физически удаляются **не здесь**, а в шаге 6.2. Сейчас `ICategoryAssignmentRepository` ещё используется `CommodityCategorizationService` — удаление раньше шага 4 сломает сборку. В плане задача 6 допускает «если не удалены в задаче 1» — оставляем на задачу 6.

---

## Задача 2. Конфигурация лимита и DI (Infrastructure)

### Шаг 2.1 — Опция `CommodityCategoryCacheOptions`

**Файлы:** создать `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/CommodityCategoryCacheOptions.cs`.

**Объём изменений** (дословно ADR 019 п.3):

```csharp
namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

/// <summary>
/// Настройки in-memory кэша категорий товаров (ADR 019).
/// Секция "CommodityCategoryCache"; env-переменная CommodityCategoryCache__MaxSize (без пересборки, стиль AI__*).
/// MaxSize < 1 — кэш отключён.
/// </summary>
public sealed class CommodityCategoryCacheOptions
{
    public const string SectionName = "CommodityCategoryCache";
    public int MaxSize { get; set; } = 1000;
}
```

**Правки тестов:** нет.

**Критерий готовности:** `cd Analytics && dotnet build` (проект Infrastructure компилируется); в дальнейшем шаг 7.1 добавит тест на значение по умолчанию `1000`.

### Шаг 2.2 — `appsettings.json` / `appsettings.Development.json`: секция `CommodityCategoryCache`

**Файлы (правка):**
- `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`;
- `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`.

**Объём изменений:** в корень JSON (на том же уровне, что `"AI"` / `"Infrastructure"`) добавить:

```json
  "CommodityCategoryCache": {
    "MaxSize": 1000
  }
```

Запятые расставить с учётом соседних секций. Умолчание `1000` продублировано и в опции, и в конфиге; изменение без пересборки — env `CommodityCategoryCache__MaxSize` (биндинг стандартного `AddEnvironmentVariables()` ASP.NET Core, префикс не нужен).

**Правки тестов:** нет.

**Критерий готовности:** обе секции видны в файлах; `jq .CommodityCategoryCache appsettings.json` (или аналогично для Development) возвращает `{"MaxSize":1000}`.

### Шаг 2.3 — DI: биндинг опций + singleton-регистрация кэша

**Файлы (правка):** `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs`.

**Объём изменений**
(ADR 019 п.3; `using Microsoft.Extensions.Options;` и `using ...Infrastructure.Modules.Commodities;` уже есть в файле):

1. В `ConfigureInfrastructureOptions` (после блока `AiOptions`) добавить:

```csharp
services.AddOptions<CommodityCategoryCacheOptions>()
    .Bind(configuration.GetSection(CommodityCategoryCacheOptions.SectionName));
```

2. В `AddInfrastructure` (после `services.ConfigureInfrastructureOptions(configuration);`) добавить singleton:

```csharp
// In-memory кэш категорий — Singleton; опция читается при старте (изменение MaxSize требует рестарта).
services.AddSingleton<ICommodityCategoryCache>(sp => new InMemoryCommodityCategoryCache(
    sp.GetRequiredService<IOptions<CommodityCategoryCacheOptions>>().Value.MaxSize));
```

3. **Здесь не трогать** строку `services.AddScoped<ICategoryAssignmentRepository, CategoryAssignmentRepository>();` (удаляется в шаге 6.2 — интерфейс ещё используется сервисом категоризации до шага 4.1).

**Правки тестов:** нет.

**Критерий готовности:** `cd Analytics && dotnet build` — зелёная сборка; `ICommodityCategoryCache` резолвится как singleton (проверяется фактически в шагах 7.2/8).

---

## Задача 3. Наполнение кэша при старте (FR-1.2, FR-1.7; Infrastructure)

### Шаг 3.1 — `CommodityCategoryCacheInitializationHostedService`

**Файлы:** создать `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/CommodityCategoryCacheInitializationHostedService.cs`.

**Объём изменений** (ADR 019 п.4; паттерн `ReceiptSynchronizationHostedService`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

/// <summary>
/// Наполняет in-memory кэш категорий при старте из commodities (FR-1.2, FR-1.7; ADR 019 п.4).
/// Fail-fast при недоступности БД (паттерн ReceiptSynchronizationHostedService).
/// </summary>
internal sealed class CommodityCategoryCacheInitializationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICommodityCategoryCache _cache;
    private readonly IOptions<CommodityCategoryCacheOptions> _options;
    private readonly ILogger<CommodityCategoryCacheInitializationHostedService> _logger;

    public CommodityCategoryCacheInitializationHostedService(
        IServiceScopeFactory scopeFactory,
        ICommodityCategoryCache cache,
        IOptions<CommodityCategoryCacheOptions> options,
        ILogger<CommodityCategoryCacheInitializationHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var maxSize = _options.Value.MaxSize;
        if (maxSize < 1)
        {
            // MaxSize < 1 — кэш отключён (инвариант 7): не наполняем, ИИ вызывается всегда.
            _logger.LogInformation("Commodity category cache is disabled (MaxSize < 1).");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();

            if (!await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Unable to connect to the analytics database. Ensure the database is created and accessible with the configured credentials.");
            }

            // FR-1.2/FR-1.7: позиции с заполненной категорией, отличной от Undefined (CategoryId != 0).
            // Проекция только нужных колонок (Name, CategoryId); OrderBy(Name) — детерминизм first-wins
            // при равных названиях.
            // ВАЖНО (ADR 019 п.4): query filter ReceiptDbContext настроен ТОЛЬКО на ReceiptEntity,
            // поэтому запрос через DbSet<CommodityEntity> охватывает позиции ВСЕХ пользователей —
            // это соответствует сквозному масштабу кэша (FR-2.4). Фильтр по userId НЕ добавляем.
            var commodities = await dbContext.Commodities
                .AsNoTracking()
                .Where(c => c.CategoryId != null && c.CategoryId != 0)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Name, c.CategoryId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var commodity in commodities)
            {
                // Ранний выход: Count == MaxSize — кэш «замёрз», дальнейшие TryAdd были бы no-op
                // (first-wins + OrderBy(Name) даёт тот же результат, что и полный перебор).
                if (_cache.Count >= maxSize)
                {
                    break;
                }

                var categoryId = commodity.CategoryId!.Value;
                if (!Enum.IsDefined(typeof(CommodityCategory), categoryId))
                {
                    continue; // невалидный CategoryId в данные попасть не должен, но защищаемся
                }

                _cache.TryAdd(
                    CommodityNameNormalizer.NormalizeName(commodity.Name),
                    (CommodityCategory)categoryId);
            }

            _logger.LogInformation(
                "Commodity category cache initialized: {Count} entries (MaxSize={MaxSize}).",
                _cache.Count, maxSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Commodity category cache initialization failed during application startup.");
            throw; // fail-fast: без БД сервис бесполезен (аналогично ReceiptSynchronizationHostedService)
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Ключевые требования ADR, обязательные к реализации: проекция `(Name, CategoryId)`; `Enum.IsDefined` для `CategoryId`; `CommodityNameNormalizer.NormalizeName` для ключа; **ранний выход** при `Count >= MaxSize`; лог `Count`; `throw` при ошибке; `StopAsync` → `Task.CompletedTask`.

**Правки тестов:** нет (тесты hosted-сервиса — шаг 7.2).

**Критерий готовности:** `cd Analytics && dotnet build` — зелёная сборка. Поведение наполнения проверяется в шаге 7.2; порядок запуска (все `StartAsync` завершаются до приёма HTTP) — за счёт ASP.NET Core, дополнительно не проверяется.

### Шаг 3.2 — DI: регистрация hosted-сервиса

**Файлы (правка):** `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` (в `AddInfrastructure`).

**Объём изменений:** после `services.AddHostedService<AdminUserHostedService>();` добавить:

```csharp
services.AddHostedService<CommodityCategoryCacheInitializationHostedService>();
```

`using ...Infrastructure.Synchronization;` уже есть в файле. Порядок относительно других hosted-сервисов не влияет на корректность: все `StartAsync` завершаются до первого HTTP-запроса.

**Правки тестов:** нет.

**Критерий готовности:** `cd Analytics && dotnet build` — зелёная сборка; при старте API (шаг 8) в логах появляется «Commodity category cache initialized: N entries».

---

## Задача 4. Подключение кэша в сервис категоризации (FR-2.1…FR-2.4; Infrastructure)

**Файлы (правка):** `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` (шаги 4.1 + 4.2 — один и тот же файл, выполняются вместе).

### Шаг 4.1 — Замена зависимости и шаг 2 `SuggestForReceiptAsync` через кэш

**Объём изменений:**

1. Поле `private readonly ICategoryAssignmentRepository _categoryAssignmentRepository;` → `private readonly ICommodityCategoryCache _cache;`, параметр конструктора — соответственно. XML-комментарий класса обновить («кэш commodity_category_assignments» → «in-memory кэш (ADR 019)»).
2. В `SuggestForReceiptAsync` блок «Шаг 2: сквозной кэш ранее присвоенных категорий» заменить с DB-репозитория на синхронный кэш:

```csharp
// Шаг 2: in-memory кэш ранее присвоенных категорий (сквозной, без userId; FR-2.4).
var aiCandidates = new List<Commodity>();
foreach (var item in pending)
{
    var normalized = CommodityNameNormalizer.NormalizeName(item.Name);
    if (_cache.TryGet(normalized, out var category))
    {
        // Дисплейное имя — из CommodityCategoryHelper, а не из БД (ADR 019, «Как новая схема устраняет причины»).
        var categoryName = CommodityCategoryHelper.GetDisplayName(category);
        results.Add(CreateResult(item, (int)category, categoryName, CategorizationSource.Cache, null));
    }
    else
    {
        aiCandidates.Add(item);
    }
}
```

Шаги 1 и 3 алгоритма, дедупликация AI-вызовов по нормализованному имени, частичный успех (UC-4) — **без изменений**. Это и есть устранение FR-2.2: при попадании в кэш ИИ не вызывается.

### Шаг 4.2 — `ApplyConfirmedCategoriesAsync`: `TryAdd` при сохранении (FR-1.3)

**Объём изменений:** внутри цикла подтверждения заменить блок записи в репозиторий на запись в кэш:

```csharp
if (assignment.Category is { } category)
{
    var normalized = CommodityNameNormalizer.NormalizeName(commodity.Name);
    _cache.TryAdd(normalized, category); // first-wins (FR-1.4); no-op при лимите (FR-1.5)
}
```

Сброс (`category == null`) в кэш **не пишется** и **не удаляется** (ADR 019, «Отрицательные последствия»). `updated++` и логика владения — без изменений.

### Правки тестов (обязательные, в этом же шаге — иначе сборка тестов красная)

`Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategorizationServiceTests.cs`:

1. Заменить `private readonly ICategoryAssignmentRepository _categoryAssignmentRepository = Substitute.For<ICategoryAssignmentRepository>();` на `private readonly ICommodityCategoryCache _cache = Substitute.For<ICommodityCategoryCache>();`.
2. В `CreateService` передать `_cache` на место третьего аргумента.
3. Переписать сценарии, обращающиеся к старому моку/доменному типу (детально — шаг 7.3; здесь минимально: убрать обращения к `_categoryAssignmentRepository` и `CommodityCategoryAssignment`, расставить мок кэша с `out`-параметром):

```csharp
_cache.TryGet("кефир", out Arg.Any<CommodityCategory>())
    .Returns(callInfo => { callInfo[1] = CommodityCategory.Dairy; return true; });
```

4. Удалить/заменить assert-вызовы `_categoryAssignmentRepository` (`DidNotReceive...GetByNormalizedNameAsync`, `Received...UpsertAsync`) на `_cache` (`DidNotReceiveWithAnyArgs().TryGet(default!, out _)`, `Received(1).TryAdd(...)`, `DidNotReceiveWithAnyArgs().TryAdd(default!, default)`).

**Критерий готовности:** `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategorizationServiceTests"` — проект собирается и тесты зелёные (после минимальной правки выше; полный набор сценариев, включая «АИ-95-К5», — шаг 7.3).

---

## Задача 5. Ручное назначение категории обновляет кэш (FR-1.3; Api)

### Шаг 5.1 — `CommodityEndpoints.UpdateCategory` пишет в кэш

**Файлы (правка):** `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Commodities/CommodityEndpoints.cs`.

**Объём изменений** (ADR 019 п.6; устраняет причину 1 из «Анализа причины»):

1. В сигнатуру `UpdateCategory` добавить `[FromServices] ICommodityCategoryCache cache`.
2. После `await commodityRepository.UpdateCategoryAsync(id, category, cancellationToken);` добавить:

```csharp
if (category != CommodityCategory.Undefined)
{
    cache.TryAdd(CommodityNameNormalizer.NormalizeName(commodity.Name), category);
}
```

`commodity` уже получен из `commodityRepository.GetByIdAsync` выше — дополнительного DB-запроса нет. `TryAdd` синхронный, `await` не нужен. Условие `!= Undefined` важно: ручной сброс в `Undefined` (0) в кэш не пишется (инвариант 8); контракт эндпоинта не меняется.

`using` уже импортированы: `ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts` (п.4 файла), `ReceiptCollector.Analytics.Domain.Modules.Commodities` (п.6).

**Правки тестов:** существующие тесты эндпоинтов не вызывают `UpdateCategory` — сборка не ломается; новые тесты — шаг 7.4.

**Критерий готовности:** `cd Analytics && dotnet build` — зелёная сборка; поведение проверяется в шаге 7.4 и в E2E (шаг 8, п.4).

---

## Задача 6. Удаление таблицы и EF/доменного кода (FR-1.6; Migrations + Infrastructure + Domain)

### Шаг 6.1 — SQL-миграция удаления таблицы

**Файлы:** создать `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20260922120000_drop_commodity_category_assignments.sql`.

**Объём изменений** (дословно ADR 019 п.7):

```sql
DROP TABLE IF EXISTS commodity_category_assignments;
```

- `IF EXISTS` — среда, где add-миграция `20260919120000_add_commodity_category_assignments.sql` не применялась, не сломается;
- данные **не переносятся** (FR-1.6/FR-1.7): источник истины — `commodities`;
- `MigrationRunner` подхватывает скрипт автоматически (сортировка по имени, `20260922120000 > 20260919120000`), правок runner не требуется.

**Правки тестов:** нет (миграции автотестами не покрываются; проверка в E2E, шаг 8 п.6).

**Критерий готовности:** `cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run` применяет скрипт без ошибок (при доступном PostgreSQL); `psql -c '\d commodity_category_assignments'` → «Did not find any relation».

> Порядок: скрипт создаётся в задаче 6, но **применяется** только после того, как код перестал обращаться к таблице (шаги 4/5 выполнены) — именно так устроен единый релиз (см. «Последовательность миграции» в плане).

### Шаг 6.2 — Удаление кода таблицы (Domain/EF/DbContext/DI) и проверка сборки

**Файлы (удалить):**
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategoryAssignment.cs`
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/ICategoryAssignmentRepository.cs`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CategoryAssignmentRepository.cs`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityCategoryAssignmentEntity.cs`
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/CommodityCategoryAssignmentConfiguration.cs`

**Файлы (правка):**
1. `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/ReceiptDbContext.cs`:
   - удалить строку `public DbSet<CommodityCategoryAssignmentEntity> CommodityCategoryAssignments => Set<CommodityCategoryAssignmentEntity>();`
   - удалить строку `modelBuilder.ApplyConfiguration(new CommodityCategoryAssignmentConfiguration());`
   - `using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;` остаётся (другие конфигурации нужны).
2. `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs`: удалить строку `services.AddScoped<ICategoryAssignmentRepository, CategoryAssignmentRepository>();` (и, при необходимости, ставший лишним using — проверить, что `Domain.Modules.Commodities` ещё нужен для других регистраций).
3. `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategorizationService.cs`: в XML-доке (строка ~28) заменить упоминание `commodity_category_assignments` на «in-memory кэш (ADR 019)» — только комментарий, сигнатуры не менять.

**Правки тестов (в связке с этим шагом, иначе сборка тестов красная):**
- удалить `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CategoryAssignmentRepositoryTests.cs` (шаг 7.5);
- убедиться, что `CommodityCategorizationServiceTests` не содержит ссылок на `CommodityCategoryAssignment` (шаг 7.3, который выполняется в рамках того же набора изменений).

**Критерий готовности:** `cd Analytics && dotnet build` — зелёная сборка **всех проектов** (включая тесты, после шагов 7.3/7.5); поиск по коду: `grep -rn "CommodityCategoryAssignment\|ICategoryAssignmentRepository" Analytics/src Analytics/tests` → ноль совпадений (кроме папки `obj/`).

---

## Задача 7. Тесты (.NET)

> Все тестовые файлы: `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/`. Паттерн — NSubstitute + `UserContext.SetUserId` (где нужен userId). EF InMemory для DbContext — пакет уже подключён (`Microsoft.EntityFrameworkCore.InMemory`).

### Шаг 7.1 — Новые тесты кэша

**Файлы:** создать `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/InMemoryCommodityCategoryCacheTests.cs`.

**Объём изменений** (класс `public class InMemoryCommodityCategoryCacheTests`, хелпер `new InMemoryCommodityCategoryCache(maxSize)`; тип `internal` доступен благодаря `InternalsVisibleTo`):

| Тест | Проверяет |
|---|---|
| `TryAdd_then_TryGet_returns_stored_category` | round-trip: `TryAdd("молоко", Dairy)` → `true`; `TryGet("молоко", out var c)` → `true`, `c == Dairy` |
| `TryGet_returns_false_for_unknown_key` | отсутствующий ключ → `false` |
| `TryAdd_is_first_wins_for_existing_key` | повторный `TryAdd("молоко", Food)` → `false`; `TryGet` всё ещё `Dairy`; `Count` не меняется (FR-1.4) |
| `TryAdd_freezes_at_max_size_without_eviction` | `MaxSize=2`: ключи 1–2 добавлены; 3-й (ранее неизвестный) → `false`; `Count==2`; существующие читаются; новых не прибавляется (FR-1.5) |
| `TryAdd_freezes_at_default_max_size_1000` | `new InMemoryCommodityCategoryCache(Options.Create(new CommodityCategoryCacheOptions()).Value.MaxSize)` (т.е. 1000): 1000-й добавлен, 1001-й → `false` (критерий приёмки «лимит по умолчанию 1000») |
| `TryAdd_rejects_undefined_category` | `TryAdd("x", Undefined)` → `false`; `Count==0` (FR-1.2) |
| `TryAdd_disables_cache_when_max_size_below_one` | `[Theory] MaxSize=0 и MaxSize=-1`: `TryAdd` → `false`, `Count==0`, `TryGet` → `false` («кэш отключён») |

### Шаг 7.2 — Новые тесты hosted-сервиса наполнения

**Файлы:** создать `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityCategoryCacheInitializationHostedServiceTests.cs`.

**Объём изменений:** InMemory-EF + реальный кэш. Хелпер:

```csharp
private static (ServiceProvider Provider, InMemoryCommodityCategoryCache Cache) CreateHost(
    int maxSize, Action<ReceiptDbContext> seed)
{
    var services = new ServiceCollection();
    services.AddDbContext<ReceiptDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
    var cache = new InMemoryCommodityCategoryCache(maxSize);
    services.AddSingleton<ICommodityCategoryCache>(cache);
    services.AddSingleton(Options.Create(new CommodityCategoryCacheOptions { MaxSize = maxSize }));

    using var scope = services.BuildServiceProvider().CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
    seed(db);
    db.SaveChanges();

    return (services.BuildServiceProvider(), cache);
}
```

Затем: `var hosted = new CommodityCategoryCacheInitializationHostedService(provider.GetRequiredService<IServiceScopeFactory>(), cache, Options.Create(...), NullLogger...); await hosted.StartAsync(ct);`.

Случаи (сеются `CommodityEntity` через `db.Commodities.AddRange`):
- **Наполнение по FR-1.2/1.7**: позиция с `CategoryId=23 (Dairy)` → ключ в кэше есть; позиция `CategoryId=null` и `CategoryId=0 (Undefined)` → в кэше нет; `CategoryId=9999` (невалидный) → в кэше нет (исключено `Enum.IsDefined`). Соответствует критерию приёмки «наполнение при старте».
- **Нормализация + first-wins при равных названиях**: две позиции `"АИ-95-К5"→Fuel` и `"аи-95-к5"→Dairy`; после `OrderBy(Name)` (ordinal: `А` U+0410 < `а` U+0430) первой обработана `"АИ-95-К5"` → `Count==1`, `TryGet("аи-95-к5")==Fuel` (первое значение выигрывает).
- **Ранний выход при лимите**: `MaxSize=2`, 5 позиций с разными именами → `Count==2`; ручной `cache.TryAdd` нового ключа → `false` («замерзание», FR-1.5).
- **Кэш отключён**: `MaxSize=0` → `Count==0`, `TryGet` → `false`, старт без исключений (инвариант 7).
- (Опционально) «сброс с `category_id=0` исключается» уже покрыто первым случаем.

### Шаг 7.3 — Обновление `CommodityCategorizationServiceTests` (включая сценарий «АИ-95-К5»)

**Файлы (правка):** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategorizationServiceTests.cs`.

**Объём изменений:**
1. Мок: `ICommodityCategoryCache _cache = Substitute.For<ICommodityCategoryCache>()`; передаётся в `CreateService`.
2. Хелпер для попадания в кэш (NSubstitute `out`-аргумент):

```csharp
private static void ArrangeCacheHit(string normalized, CommodityCategory category)
{
    _cache.TryGet(normalized, out Arg.Any<CommodityCategory>())
        .Returns(callInfo => { callInfo[1] = category; return true; });
}
```

3. Сценарии:

| Тест | Проверяет |
|---|---|
| `Suggest_uses_existing_category_without_ai_or_cache` | существующая категория → `source=existing`; ИИ не вызван; `_cache.DidNotReceiveWithAnyArgs().TryGet(default!, out _)` (кэш не опрашивается — приоритет «existing → cache → ai», FR-2.1) |
| `Suggest_uses_cache_and_skips_ai_for_ai95k5` | **сценарий «АИ-95-К5»** (FR-2.2): позиция `"АИ-95-К5"` без категории; `ArrangeCacheHit("аи-95-к5", Fuel)` → `source=cache`, `CategoryId == (int)Fuel`, `CategoryName == "Топливо"`, `_aiClient.DidNotReceiveWithAnyArgs()` |
| `Suggest_treats_undefined_category_as_missing_and_uses_cache` | позиция с `Undefined` (`category_id=0`) трактуется как «без категории» → попадание в кэш (`кефир`→Dairy) → `source=cache`, ИИ не вызван |
| `Suggest_falls_back_to_ai_when_no_existing_category_and_no_cache` | промах кэша → ИИ вызывается, `source=ai` (регрессия шага 3) |
| `Suggest_marks_undefined_when_ai_fails` | сбой ИИ → `source=undefined`, `error` заполнен (UC-4, без изменений) |
| `Suggest_preserves_original_item_order_and_deduplicates_ai_calls` | порядок позиций и дедупликация AI-вызовов по нормализованному имени (без изменений логики; кэш пустой) |
| `Suggest_passes_catalog_of_categories_without_undefined_to_ai` | каталог `GetAll()` без `Undefined` (без изменений) |
| `Suggest_does_not_write_anything` | `_commodityRepository.DidNotReceive...UpdateCategoryAsync`; `_cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default)` |
| `ApplyConfirmedCategories_saves_categories_and_updates_cache` | после `UpdateCategoryAsync` → `_cache.Received(1).TryAdd("сыр", Dairy)` (FR-1.3) |
| `ApplyConfirmedCategories_null_category_resets_without_cache_update` | сброс (`null`) → `_cache.DidNotReceiveWithAnyArgs().TryAdd(...)` (сброс не пишется в кэш) |
| `ApplyConfirmedCategories_skips_commodities_not_belonging_to_receipt` | чужие `commodityId` → ни `UpdateCategoryAsync`, ни `TryAdd` (защита от массового обновления) |
| `ApplyConfirmedCategories_rejects_explicit_undefined_category` / `..._throws_ReceiptNotFoundException...` | без изменений |

### Шаг 7.4 — Обновление `CommodityEndpointsTests` (PUT пишет в кэш)

**Файлы (правка):** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityEndpointsTests.cs`.

**Объём изменений:** добавить моки `IUserRepository`, `ICommodityRepository`, `ICommodityCategoryCache` и тесты `UpdateCategory` (паттерн — как в `MerchantEndpointsTests`: `new User(userId, "admin", "ext", isAdmin: true)` + `UserContext.SetUserId`):

| Тест | Проверяет |
|---|---|
| `UpdateCategory_writes_category_to_cache` | админ, товар `"АИ-95-К5"`, категория `Fuel` → `Ok`; `_cache.Received(1).TryAdd("аи-95-к5", Fuel)` (FR-1.3, причина 1 из ADR 019) |
| `UpdateCategory_does_not_write_undefined_to_cache` | `CategoryId=0` (`Undefined`) → `Ok`; `_cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default)` (инвариант 8) |
| `UpdateCategory_returns_forbidden_for_non_admin` | `isAdmin:false` → `Forbid`, кэш не трогается (регрессия прав доступа) |

Тестовая арранжировка: `_commodityRepository.GetByIdAsync(commodityId, ct)` → `new Commodity(commodityId, receiptId, "АИ-95-К5", 1m, 100m, 0, 0m)`; `_userRepository.GetByIdAsync(userId, ct)` → админ. Вызов `CommodityEndpoints.UpdateCategory(commodityId, new UpdateCategoryRequest((int)Fuel), _commodityRepository, _userRepository, _cache, ct)`.

### Шаг 7.5 — Удаление репозиторных тестов и полный прогон

**Файлы:** удалить `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CategoryAssignmentRepositoryTests.cs` (таблица и EF-код удалены — шаг 6.2).

**Объём изменений:** больше ничего.

**Критерий готовности (для шагов 7.1–7.5):**

```bash
cd Analytics && dotnet build && dotnet test
```

Все тесты зелёные, включая архитектурные `ProjectDependencyTests` (контракт — `Application`, реализация — `Infrastructure`, слои не нарушены) и `CommodityNameNormalizerTests`/`CommodityCategoryTests` (не менялись, регрессии нет).

---

## Задача 8. Ручное тестирование end-to-end (критерии приёмки задачи)

**Окружение:** PostgreSQL доступен; миграции применены (включая `20260922120000_drop_commodity_category_assignments.sql`); API запущен (`cd Analytics/src/ReceiptCollector.Analytics.Api && dotnet run`, или `./up.dev.sh`); frontend доступен; `AI:BaseUrl` сконфигурирован.

**Чек-лист (соответствует критериям приёмки `docs/tasks/in-memory-commodity-category-cache.md`):**

1. **Наполнение при старте**: в логах API — `Commodity category cache initialized: N entries`; позиции с категорией из `commodities` отдаются `source=cache` (в чужом чеке без собственной категории) или `source=existing` (в своём), ИИ не вызывается.
2. **Сценарий «АИ-95-К5»**: в `commodities` есть/через сохранение добавлена позиция `"АИ-95-К5"` с категорией `Топливо` (Fuel); в новом чеке позиция `"АИ-95-К5"` без категории → после `POST /api/receipts/{id}/categories/suggest` в ответе `"source":"cache"`, `"category":"Fuel"`, ИИ **не** вызван (проверяется логами/метриками и отсутствием задержки ИИ).
3. **Новое название** (нет в кэше) → `"source":"ai"`, формат ответа не изменился (`{"category":"Name"}`).
4. **Ручной PUT**: `PUT /api/commodities/{id}/category` (admin) с `{"categoryId":14}` для `"АИ-95-К5"` → повторный suggest этого названия в другом чеке даёт `source=cache` (устранение причины 1 из ADR 019).
5. **Лимит**: рестарт API с `CommodityCategoryCache__MaxSize=10` → после 10 записей кэш «замерзает», новые названия `source=ai`, вытеснения нет; лимит применён **без пересборки**.
6. **Миграция**: `psql -c '\d commodity_category_assignments'` → таблица отсутствует; в логах `suggest` нет ошибок обращения к таблице; `grep -rn "commodity_category_assignments" Analytics/src Analytics/tests` → ноль совпадений в коде.
7. **Полный прогон**: `cd Analytics && dotnet test` — все тесты проходят.
8. **Регрессия**: порт `5039`, webUI «Категоризировать»/«Сохранить», списочное представление позиций с фильтрами — без изменений; frontend/Go/bot/nginx не менялись.

**Ролбэк (если потребуется):** перед деплоем снять `pg_dump -t commodity_category_assignments` (см. «Последовательность миграции» в плане); при откате — восстановить таблицу и откатить бинарник.

---

## 9. Порядок выполнения и зависимости

| Шаг | Содержание | Зависит от | Можно параллелить с |
|---|---|---|---|
| 1.1 | Контракт `ICommodityCategoryCache` | — | 2.1, 2.2 |
| 1.2 | Реализация `InMemoryCommodityCategoryCache` | 1.1 | 2.1, 2.2 |
| 2.1 | Опция `CommodityCategoryCacheOptions` | — | 1.1, 1.2, 2.2 |
| 2.2 | `appsettings*.json`: секция | — | 1.1, 1.2, 2.1 |
| 2.3 | DI: options + singleton кэша | 1.1, 1.2, 2.1 | — |
| 3.1 | Hosted-сервис наполнения | 1.1, 1.2, 2.1 | — |
| 3.2 | DI: `AddHostedService` | 3.1, 2.3 | — |
| **4.1+4.2** | `CommodityCategorizationService`: шаг 2 через кэш + `TryAdd` при сохранении (+ минимальные правки тестов) | 1.1, 1.2, 3.x | **5.1**, 6.1 |
| **5.1** | `CommodityEndpoints.UpdateCategory` → кэш | 1.1 | **4.1+4.2**, 6.1 |
| 6.1 | SQL-drop-миграция (файл) | 4.x, 5.1 (код перестал обращаться к таблице) | — |
| 6.2 | Удаление Domain/EF/DbContext/DI-кода | 4.x, 5.1 | — |
| 7.1 | Тесты кэша | 1.2, 2.1 | 7.2, 7.3, 7.4 |
| 7.2 | Тесты hosted-сервиса | 3.1 | 7.1, 7.3, 7.4 |
| 7.3 | `CommodityCategorizationServiceTests` (АИ-95-К5) | 4.x | 7.1, 7.2, 7.4 |
| 7.4 | `CommodityEndpointsTests` (PUT→кэш) | 5.1 | 7.1, 7.2, 7.3 |
| 7.5 | Удаление `CategoryAssignmentRepositoryTests` + `dotnet test` | 6.2, 7.1–7.4 | — |
| 8 | Ручная E2E-приёмка | 6.1, 7.5 | — |

**Критический путь:** 1.1 → 1.2 → 2.3 → 3.1 → 3.2 → 4 → 6 → 7 → 8.

**Ключевые правила:**
- Шаги 1.1/1.2/2.1/2.2 — «штык»; шаг 2.3 склеивает их.
- Шаги 4 и 5 — независимы и параллелизуются (4 меняет Infrastructure, 5 — Api; оба опираются только на шаги 1–3).
- Шаг 6.2 **нельзя** начать раньше 4/5 (код ещё вызывает `ICategoryAssignmentRepository`) и **нельзя** завершить без правок тестов 7.3/7.5 (доменные типы удаляются) — рекомендуется выполнять 6.2 и задачу 7 в одном PR.
- Шаг 7 — целиком параллелизуем внутри, финальная сборка проверяется в 7.5.
- Шаг 8 — только после применения drop-миграции (6.1) и зелёного `dotnet test` (7.5).

## 10. Связь с документами

- Задача: [`docs/tasks/in-memory-commodity-category-cache.md`](../tasks/in-memory-commodity-category-cache.md)
- ADR: [`docs/adr/019-in-memory-commodity-category-cache.md`](../adr/019-in-memory-commodity-category-cache.md)
- План (оценки/приоритеты/критический путь/миграция): [`docs/plans/in-memory-commodity-category-cache.md`](in-memory-commodity-category-cache.md)
- ADR 009 (заменяемые решения C1/C4): [`docs/adr/009-auto-commodity-categorization.md`](../adr/009-auto-commodity-categorization.md)