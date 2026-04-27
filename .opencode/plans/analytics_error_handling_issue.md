# Задача: Исправить обработку ошибок и восстановление

- **Приоритет**: HIGH
- **Цель**: Устранить несоответствия в обработке ошибок, которые могут приводить к крашам приложения или скрытым сбоям.

### Описание проблемы

- **Локация**: `Analytics/src/ReceiptCollector.Analytics.Migrations/MigrationRunner.cs` (строки 85-107)
- **Текущий код**:
```csharp
try
{
    await ExecuteScriptAsync(connection, transaction, scriptContent, cancellationToken).ConfigureAwait(false);
    await RecordScriptAsync(connection, transaction, script.Name, cancellationToken).ConfigureAwait(false);
    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    _logger.LogInformation("Script {ScriptName} applied successfully.", script.Name);
}
catch (Exception ex)
{
    try
    {
        if (transaction.Connection is not null)  // BUG: Always null
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
    }
    catch (Exception rollbackEx)
    {
        _logger.LogError(rollbackEx, "Failed to rollback transaction for script {ScriptName}.", script.Name);
    }

    _logger.LogError(ex, "Failed to apply script {ScriptName}. Transaction rolled back.", script.Name);
    throw;  // Re-throws immediately
}
```
- **Проблема**:
  - Транзакции не правильно откатываются (как ранее идентифицировано)
  - Отсутствует логика повторных попыток для временных сбоев
  - Непосредственное перевыбрасывание без попытки восстановления
  - Отсутствует паттерн прерывателя цепи
  - Сервисы полностью отказывают вместо плавного снижения функциональности при недоступности зависимостей

### План решения

- **Шаг 1**: Реализовать политику повторных попыток:
```csharp
// Добавить в конфигурацию DI
services.AddTransient<AsyncRetryPolicy>(sp => Policy
    .Handle<NpgsqlException>()
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));
```

- **Шаг 2**: Исправить логику отката транзакций (как идентифицировано в задаче миграции)

- **Шаг 3**: Добавить прерыватель цепи для операций с базой данных:
```csharp
services.AddTransient<AsyncCircuitBreakerPolicy>(sp => Policy
    .Handle<NpgsqlException>()
    .CircuitBreakerAsync(3, TimeSpan.FromMinutes(1), OnBreak, OnReset, OnHalfOpen));
```

- **Шаг 4**: Реализовать плавное снижение функциональности:
  - Кэшировать последнее известное хорошее состояние
  - Возвращать устаревшие данные при сбое основного источника
  - Логировать события снижения функциональности

### Тестирование

- **Команды**:
  - Симулировать сбои базы данных и проверить поведение повторных попыток
  - Проверять срабатывание и сброс прерывателя цепи
  - Тестировать сценарии плавного снижения функциональности
- **Ожидаемые результаты**:
  - Повторные попытки работают корректно при временных сбоях
  - Прерыватель цепи срабатывает и сбрасывается правильно
  - Плавное снижение функциональности работает в ожидаемых сценариях
  - Транзакции откатываются правильно при сбое

### Критерии успеха
- Реализована политика повторных попыток для базовых операций
- Логика отката транзакций исправлена
- Прерыватель цепи работает корректно
- Плавное снижение функциональности реализовано и протестировано
