# Добавление флага пропуска синхронизации чеков

## Описание

Добавить конфигурационный флаг `Skip` в `ReceiptSynchronizationOptions`, позволяющий пропустить синхронизацию чеков из MongoDB в PostgreSQL при старте сервиса Analytics.

## Затрагиваемые файлы

| Файл | Изменения |
|------|-----------|
| `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/ReceiptSynchronizationOptions.cs` | Добавить свойство `Skip` |
| `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/ReceiptSynchronizationHostedService.cs` | Внедрить `IOptions<T>`, проверять флаг в `StartAsync` |
| `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json` | (Опционально) Добавить секцию для разработчика |

## Приоритет

**P3** — Улучшение качества жизни разработчика (developer experience)

## Зависимости

- Нет внешних зависимостей
- Не требует изменений в других микросервисах (Backend, Bot)

## Шаги выполнения

### Шаг 1. Добавить свойство `Skip` в `ReceiptSynchronizationOptions`

**Файл:** `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/ReceiptSynchronizationOptions.cs`

Добавить свойство `Skip` readonly-инициализацией (init-only property) и значением по умолчанию `false`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

public sealed class ReceiptSynchronizationOptions
{
    public const string SectionName = "Infrastructure:Receipts:Synchronization";

    [Range(1, int.MaxValue)]
    public int BatchSize { get; init; } = 100;

    [Required]
    public Guid? UserId { get; init; }

    /// <summary>
    /// Если <c>true</c>, синхронизация чеков при старте сервиса пропускается.
    /// По умолчанию <c>false</c> — синхронизация выполняется.
    /// </summary>
    public bool Skip { get; init; } = false;
}
```

**Почему `init`:** свойство read-only после создания объекта, что соответствует общему стилю в проекте и гарантирует неизменяемость опций после инициализации.

**Почему `false` по умолчанию:** синхронизация должна выполняться, если флаг явно не установлен — сохранение обратной совместимости.

---

### Шаг 2. Внедрить `IOptions<ReceiptSynchronizationOptions>` и реализовать проверку в `ReceiptSynchronizationHostedService`

**Файл:** `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/ReceiptSynchronizationHostedService.cs`

Добавить зависимость от `IOptions<ReceiptSynchronizationOptions>` и изменить `StartAsync`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal sealed class ReceiptSynchronizationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReceiptSynchronizationHostedService> _logger;
    private readonly IOptions<ReceiptSynchronizationOptions> _options;

    public ReceiptSynchronizationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReceiptSynchronizationHostedService> logger,
        IOptions<ReceiptSynchronizationOptions> options)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.Skip)
        {
            _logger.LogInformation("Receipt synchronization skipped due to Skip flag.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();

            if (!await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Unable to connect to the analytics database. " +
                    "Ensure the database is created and accessible with the configured credentials.");
            }

            var synchronizationService = scope.ServiceProvider.GetRequiredService<ReceiptSynchronizationService>();
            await synchronizationService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receipt synchronization failed during application startup.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

**Ключевые моменты реализации:**

1. Проверка флага **до** создания scope — чтобы вообще не трогать БД, если синхронизация не нужна.
2. `return` вместо `Task.CompletedTask` — метод `async Task` позволяет использовать ранний return без лишних аллокаций.
3. Логирование на уровне `Information` — разработчик видит в консоли, что синхронизация пропущена.
4. Оригинальный `try/catch` блок остаётся **без изменений** — вся существующая логика сохраняется.

---

### Шаг 3. (Опционально) Добавить флаг в `appsettings.Development.json`

**Файл:** `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`

Добавить секцию `Synchronization` в `Infrastructure:Receipts`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Infrastructure": {
    "Receipts": {
      "Mongo": {
        "ConnectionString": "mongodb://user:user@localhost:27017/receipt_collection",
        "Database": "receipt_collection",
        "Collection": "receipt_requests"
      },
      "Synchronization": {
        "Skip": true
      }
    },
    "Postgres": {
      "ConnectionString": "Host=localhost;Port=5432;Database=receipts;Username=admin;Password=secret"
    },
    "AuthLinks": {
      "BaseUrl": "http://localhost:8080",
      "LifetimeMinutes": 15
    },
    "AdminUsers": {
      "TelegramIds": [
        136871539
      ]
    }
  }
}
```

**Важно:** `appsettings.Development.json` применяется **только** при запуске в `Development`-окружении. В production (где обычно используется `ASPNETCORE_ENVIRONMENT=Production` или не задано) этот файл игнорируется, и синхронизация работает как обычно.

---

### Шаг 4. Альтернативная конфигурация через переменные окружения

Для docker-compose, CI/CD или launch profiles (без правки JSON):

```bash
# Linux/macOS
export Infrastructure__Receipts__Synchronization__Skip=true

# Windows PowerShell
$env:Infrastructure__Receipts__Synchronization__Skip = "true"

# docker-compose.yml
services:
  analytics-api:
    environment:
      - Infrastructure__Receipts__Synchronization__Skip=true
```

**Соглашение:** .NET использует `__` (двойное подчёркивание) как разделитель секций в переменных окружения. Это стандартный механизм, поддерживаемый `IConfiguration`.

---

## Проверка корректности (Checklist)

- [ ] Проект компилируется без ошибок
- [ ] При `Skip = false` (по умолчанию) `ReceiptSynchronizationHostedService.StartAsync` выполняет синхронизацию как раньше — проверка подключения к БД, вызов `SynchronizeAsync`
- [ ] При `Skip = true` синхронизация не выполняется, в логе появляется `"Receipt synchronization skipped due to Skip flag."`
- [ ] Флаг устанавливается через `appsettings.Development.json` (ключ `Infrastructure:Receipts:Synchronization:Skip`)
- [ ] Флаг устанавливается через переменную окружения (ключ `Infrastructure__Receipts__Synchronization__Skip`)
- [ ] При `Skip = true` создание scope и проверка БД не выполняются (ранний return)
- [ ] Все существующие тесты проходят без изменений

## Тестирование

### Ручное тестирование

1. Запустить Analytics без флага → синхронизация выполняется.
2. Установить `Skip=true` в `appsettings.Development.json` → сервис стартует без синхронизации, в логе видно сообщение о пропуске.
3. Запустить через docker-compose с `Infrastructure__Receipts__Synchronization__Skip=true` → то же поведение.

### Unit-тесты

Для `ReceiptSynchronizationHostedService` можно добавить тест (если существующий тестовый проект покрывает hosted service):

```csharp
// Пример теста (при наличии соответствующей тестовой инфраструктуры)
[Fact]
public async Task StartAsync_WhenSkipIsTrue_ShouldNotSynchronize()
{
    // Arrange
    var options = Options.Create(new ReceiptSynchronizationOptions { Skip = true });
    var service = new ReceiptSynchronizationHostedService(
        Mock.Of<IServiceScopeFactory>(),
        Mock.Of<ILogger<ReceiptSynchronizationHostedService>>(),
        options);

    // Act
    await service.StartAsync(CancellationToken.None);

    // Assert
    // Проверяем, что синхронизация не была вызвана
    // (зависит от существующей тестовой инфраструктуры)
}
```

**Примечание:** текущий тестовый проект `ReceiptCollector.Analytics.Api.Tests` содержит тесты для `ReceiptSynchronizationService`, но не для `ReceiptSynchronizationHostedService`. Если hosted service ранее не тестировался, написание unit-теста желательно, но выходит за рамки данной задачи.

## Критерии приёмки (Definition of Done)

1. **`ReceiptSynchronizationOptions.Skip`** — добавлено свойство со значением по умолчанию `false`.
2. **`ReceiptSynchronizationHostedService`** — внедрён `IOptions<ReceiptSynchronizationOptions>`, реализована проверка в `StartAsync`.
3. **Регрессия отсутствует** — при `Skip = false` поведение идентично текущему (синхронизация выполняется).
4. **Логирование** — при `Skip = true` в лог пишется информационное сообщение.
5. **Документация** — ADR зафиксирован в `docs/adr/007-skip-receipt-synchronization-flag.md`.

## Оценка времени

| Шаг | Время |
|-----|-------|
| Шаг 1: Добавить свойство в Options | 5 мин |
| Шаг 2: Внедрение IOptions и проверка в StartAsync | 15 мин |
| Шаг 3: (Опционально) appsettings.Development.json | 5 мин |
| Проверка сборки и ручное тестирование | 10 мин |
| **Итого** | **~35 мин** |
