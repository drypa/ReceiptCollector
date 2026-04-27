# Задача: Исправить конфигурацию логирования

- **Приоритет**: HIGH
- **Цель**: Устранить несоответствия и недостатки в конфигурации логирования, которые усложняют отладку.

### Описание проблемы

- **Локация**: `Analytics/src/ReceiptCollector.Analytics.Migrations/Program.cs` (строки 20-35)
- **Текущий код**:
```csharp
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
```
- **Проблема**:
  - Используется `AddSimpleConsole` вместо правильного структурированного логирования
  - Отсутствует форматирование JSON для более простого парсинга
  - Отсутствуют идентификаторы корреляции для распределенной трассировки
  - Нет конфигурации уровней логирования
  - Несоответствие в логировании между проектами (миграции используют простое консольное логирование, API может использовать другую конфигурацию)
  - Отсутствует централизованная стратегия логирования

### План решения

- **Шаг 1**: Стандартизировать на Serilog:
```csharp
// Добавить в Program.cs
builder.Host.UseSerilog((ctx, lc) => lc
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss } {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .ReadFrom.Configuration(ctx.Configuration));
```

- **Шаг 2**: Настроить уровни логирования:
```csharp
builder.Services.Configure<LoggerFilterOptions>(options =>
{
    options.MinLevel = LogLevel.Information;
    options.Rules.Add(new LoggerFilterRule("Microsoft", LogLevel.Warning));
    options.Rules.Add(new LoggerFilterRule("System", LogLevel.Warning));
});
```

- **Шаг 3**: Добавить идентификаторы корреляции:
```csharp
builder.Host.UseSerilog((ctx, lc) => lc
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()
    .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss } {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .ReadFrom.Configuration(ctx.Configuration));
```

- **Шаг 4**: Добавить логирование проверок здоровья:
```csharp
services.AddHealthChecks()
    .AddDbContextCheck<ReceiptDbContext>(options => options.ResultStatusCodes[HealthCheckResult.Healthy] = StatusCodes.Status200OK)
    .AddNpgSql(connectionString, healthQuery: "SELECT 1", name: "postgres-db");
```

### Тестирование

- **Команды**:
  - Запуск приложения и проверка формата JSON логов
  - Проверка распространения идентификаторов корреляции через запросы
  - Проверка фильтрации уровней логирования
  - Проверка логирования проверок здоровья в логах
- **Ожидаемые результаты**:
  - Логи имеют правильный формат JSON
  - Идентификаторы корреляции распространяются через запросы
  - Уровни логирования фильтруются корректно
  - Проверки здоровья логируются правильно

### Критерии успеха
- Реализовано структурированное логирование с Serilog
- Логи имеют формат JSON
- Идентификаторы корреляции работают корректно
- Уровни логирования настроены правильно
- Проверки здоровья логируются адекватно
