# Исправление Race Condition при валидации токена

## Описание проблемы

В методе `ValidateAsync` сервиса `UserAuthLinkService` (строки 97-102) существует race condition:

```csharp
if (link.IsUsed)  // <-- проверка
{
    return UserAuthLinkValidationResult.Failure("Token already used.");
}

await _authLinkRepository.MarkAsUsedAsync(link.Id, utcNow, cancellationToken);  // <-- использование
```

Между проверкой `IsUsed` и вызовом `MarkAsUsedAsync` существует временное окно, в которое два параллельных запроса могут одновременно пройти проверку `IsUsed = false`, и оба успешно аутентифицироваться.

## Название

**Исправление Race Condition при валидации токена аутентификации**

## Приоритет

**P0** - Критическая уязвимость безопасности

## Зависимости

- Нет внешних зависимостей

## Шаги выполнения

### 1. Добавление атомарного метода в интерфейс репозитория

**Файл:** `src/ReceiptCollector.Analytics.Domain/Modules/Users/IUserAuthLinkRepository.cs`

Добавить новый метод в интерфейс:

```csharp
Task<bool> TryMarkAsUsedAsync(Guid linkId, DateTimeOffset usedAt, CancellationToken cancellationToken);
```

### 2. Реализация атомарного метода в репозитории

**Файл:** `src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/UserAuthLinkRepository.cs`

Реализовать метод с использованием атомарной операции `ExecuteUpdateAsync`:

```csharp
public async Task<bool> TryMarkAsUsedAsync(Guid linkId, DateTimeOffset usedAt, CancellationToken cancellationToken)
{
    var utcNow = usedAt;
    var affectedRows = await _dbContext.UserAuthLinks
        .Where(link => link.Id == linkId && link.UsedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(l => l.UsedAt, utcNow), cancellationToken)
        .ConfigureAwait(false);

    return affectedRows > 0;
}
```

### 3. Обновление сервиса

**Файл:** `src/ReceiptCollector.Analytics.Infrastructure/Modules/Users/UserAuthLinkService.cs`

Обновить метод `ValidateAsync`:

```csharp
// Убрать проверку link.IsUsed - теперь атомарная операция вернёт false, если токен уже использован
var success = await _authLinkRepository.TryMarkAsUsedAsync(link.Id, utcNow, cancellationToken).ConfigureAwait(false);

if (!success)
{
    return UserAuthLinkValidationResult.Failure("Token already used.");
}
```

### 4. Обновление тестов

**Файл:** `tests/ReceiptCollector.Analytics.Api.Tests/UserAuthLinkServiceTests.cs`

- Обновить существующие тесты на новую логику
- Добавить тест, проверяющий, что параллельные запросы не могут использовать один токен дважды

## Критерии приёмки

1. **Атомарность:** Метод `TryMarkAsUsedAsync` использует `ExecuteUpdateAsync` с условием `WHERE UsedAt = null`, что гарантирует атомарность операции
2. **Тест race condition:** Существует тест, проверяющий, что параллельные вызовы `ValidateAsync` с одинаковым токеном не приводят к двойной аутентификации
3. **Обратная совместимость:** Семантика метода `ValidateAsync` остаётся прежней - возвращает `Failure` для уже использованных токенов
4. **Компиляция:** Проект компилируется без ошибок

## Оценка времени

- Добавление метода в интерфейс: 15 минут
- Реализация в репозитории: 30 минут
- Обновление сервиса: 15 минут
- Обновление/добавление тестов: 45 минут

**Итого:** ~1.5 часа