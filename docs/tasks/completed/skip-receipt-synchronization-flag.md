# Задача: Флаг пропуска синхронизации чеков при старте Analytics

## Бизнес-задача

При запуске сервиса Analytics (`ReceiptCollector.Analytics.Api`) каждый раз выполняется `ReceiptSynchronizationHostedService`, который синхронизирует данные из MongoDB в PostgreSQL. В режиме разработки (debug) это занимает значительное время и замедляет цикл отладки — разработчику приходится ждать завершения синхронизации, даже если база уже актуальна.

### Ценность для заказчика (разработчика)

- **Ускорение цикла разработки**: возможность запустить Analytics в debug-режиме без синхронизации сокращает время старта с десятков секунд до секунд.
- **Гибкость конфигурации**: разработчик сам решает, нужна ли синхронизация при конкретном запуске, без правки кода.
- **Сохранение поведения по умолчанию**: в production (и при обычном деплое) синхронизация продолжает работать как раньше — изменение не ломает существующую логику.

## Варианты реализации (без глубоких технических деталей)

1. **Переменная окружения / настройка `SkipReceiptSynchronization`**: добавить флаг в конфигурацию (appsettings + переменная окружения), при установке `true` хостед-сервис пропускает синхронизацию. Подход соответствует текущей архитектуре .NET-сервиса, где конфигурация уже централизована через `IOptions<T>`.

2. **Условная регистрация сервиса**: проверять флаг в `DependencyInjectionExtensions` и не регистрировать `ReceiptSynchronizationHostedService` вовсе. Это грубее и не позволит включить синхронизацию без перезапуска.

**Предпочтительный вариант: 1** — проверка флага внутри `StartAsync` сервиса. Минимальные изменения, сохранение единого pipeline регистрации, возможность легко логировать факт пропуска.

### Детальная реализация

#### 1. Добавить свойство `Skip` в `ReceiptSynchronizationOptions`

Файл: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/ReceiptSynchronizationOptions.cs`

Добавить свойство:
```csharp
public bool Skip { get; init; } = false;
```

Значение по умолчанию `false` — синхронизация выполняется всегда, если явно не указано иное.

#### 2. Внедрить `IOptions<ReceiptSynchronizationOptions>` в `ReceiptSynchronizationHostedService`

Файл: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/ReceiptSynchronizationHostedService.cs`

- Добавить зависимость от `IOptions<ReceiptSynchronizationOptions>` в конструктор.
- В начале `StartAsync` проверить `_options.Value.Skip`:
  - Если `true` — залогировать `"Receipt synchronization skipped due to Skip flag."` и вернуть `Task.CompletedTask`.
  - Если `false` — выполнить синхронизацию как обычно.

#### 3. Секция конфигурации

Убедиться, что секция `Infrastructure:Receipts:Synchronization` уже зарегистрирована в `DependencyInjectionExtensions.ConfigureInfrastructureOptions` и биндится на `ReceiptSynchronizationOptions` — это уже есть.

#### 4. Конфигурация в appsettings / переменная окружения

Пример для `appsettings.Development.json`:
```json
"Infrastructure": {
  "Receipts": {
    "Synchronization": {
      "Skip": true
    }
  }
}
```

Через переменную окружения (удобно для docker-compose / launch profiles):
```
Infrastructure__Receipts__Synchronization__Skip=true
```

## Файлы, которые будут изменены

| Файл | Изменения |
|------|-----------|
| `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/ReceiptSynchronizationOptions.cs` | Добавить свойство `Skip` |
| `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/ReceiptSynchronizationHostedService.cs` | Проверка флага в `StartAsync` |

## Критерии успеха

1. При `Skip = false` (по умолчанию) синхронизация выполняется как и раньше — регрессии нет.
2. При `Skip = true` (через appsettings или переменную окружения) синхронизация не выполняется, сервис запускается немедленно с логом о пропуске.
3. Флаг можно установить через:
   - `appsettings.Development.json`
   - Переменную окружения (для docker-compose / CI / production)
4. Все существующие тесты проходят.
5. При `Skip = false` подключение к БД и синхронизация работают как прежде (без изменений логики).
