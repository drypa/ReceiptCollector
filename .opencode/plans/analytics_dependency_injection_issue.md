# Задача: Исправить конфигурацию внедрения зависимостей

- **Приоритет**: HIGH
- **Цель**: Устранить критическую ошибку в конфигурации PostgreSQL database context.

### Описание проблемы

- **Локация**: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` (строки 49-60)
- **Текущий код**:
```csharp
services.AddDbContext<ReceiptDbContext>((sp, builder) =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("Postgres connection string is not configured.");
    }

    builder
        .UseNpgsql(options.ConnectionString)
        .UseSnakeCaseNamingConvention();
});
```
- **Проблема**: 
  - Отсутствует указание времени жизни DbContext, что может привести к:
    - Утечкам памяти
    - Истощению пула соединений
    - Проблемам с потокобезопасностью в веб-приложениях
  - Отсутствует конфигурация автоматических миграций, что мешает приложению обнаруживать несоответствия схемы базы данных.

### План решения

- **Шаг 1**: Добавить указание времени жизни:
```csharp
services.AddDbContext<ReceiptDbContext>((sp, builder) =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("Postgres connection string is not configured.");
    }

    builder
        .UseNpgsql(options.ConnectionString)
        .UseSnakeCaseNamingConvention();
}, ServiceLifetime.Scoped);  // Добавить эту строку
```

- **Шаг 2**: Включить миграции:
```csharp
services.AddDbContext<ReceiptDbContext>((sp, builder) =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException("Postgres connection string is not configured.");
    }

    builder
        .UseNpgsql(options.ConnectionString)
        .UseSnakeCaseNamingConvention()
        .EnableSensitiveDataLogging(false)  // Для разработки только
        .EnableDetailedErrors(false);      // Для production
}, ServiceLifetime.Scoped);
```

- **Шаг 3**: Добавить проверки здоровья (опциональное улучшение):
```csharp
services.AddHealthChecks()
    .AddDbContextCheck<ReceiptDbContext>();
```

### Тестирование

- **Команды**:
  - Запуск приложения и проверка логирования DbContext
  - Нагрузка на систему для проверки пула соединений
  - Проверка применения миграций
- **Ожидаемые результаты**:
  - DbContext правильно скопирован в веб-запросах
  - Пулы соединений работают корректно под нагрузкой
  - Миграции обнаруживаются и применяются правильно
  - Нет утечек памяти при стресс-тестировании

### Критерии успеха
- DbContext зарегистрирован с правильным временем жизни (Scoped)
- Миграции включаются автоматически
- Проверки здоровья работают корректно
- Нет утечек памяти и проблем с соединениями
