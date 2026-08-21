# Заголовок
- **Приоритет**: HIGH
- **Цель**: Исправить критическую ошибку в логике выполнения миграционных скриптов, которая мешает корректному откату транзакций при сбоях.

## Описание проблемы
- **Локация**: `Analytics/src/ReceiptCollector.Analytics.Migrations/MigrationRunner.cs` (строки 92-103)
- **Текущий код**:
```csharp
catch (Exception ex)
{
    try
    {
        if (transaction.Connection is not null)  // BUG: Это всегда будет null
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
    }
    catch (Exception rollbackEx)
    {
        _logger.LogError(rollbackEx, "Failed to rollback transaction for script {ScriptName}.", script.Name);
    }

    _logger.LogError(ex, "Failed to apply script {ScriptName}. Transaction rolled back.", script.Name);
    throw;
}
```
- **Проблема**: При возникновении исключения во время выполнения SQL-скрипта код пытается откатить транзакцию, но проверяет `transaction.Connection`, который всегда null после создания транзакции. Это приводит к тому, что транзакция не откатывается при сбоях.

## План решения
- **Шаг 1**: Удалить проверку на null для `transaction.Connection` и напрямую вызывать `RollbackAsync()`.
- **Шаг 2**: Добавить логику повторных попыток для временных ошибок подключения к базе данных.
- **Шаг 3**: Дополнить логирование детальной информацией об ошибках, включая неудавшийся SQL-запрос.

## Тестирование
- **Команды**:
```bash
cd Analytics/src/ReceiptCollector.Analytics.Migrations
dotnet test
```
- **Ожидаемые результаты**: Все тесты должны проходить успешно, включая:
1. Успешное выполнение скрипта и фиксация транзакции
2. Корректный откат транзакции при сбое
3. Правильный порядок выполнения нескольких скриптов
4. Пропуск уже применённых скриптов

## Критерии успеха
- Все тесты проходят успешно
- Транзакции корректно откатываются при сбоях
- Логи содержат детальную информацию об ошибках
