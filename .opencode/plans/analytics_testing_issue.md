# Задача: Исправить проблему с инфраструктурой тестирования

## Общие принципы

1. **Ясность и понятность**: Задача описана на русском языке, без использования эмодзи.
2. **Уровень детализации**: Описание достаточно подробным для реализации младшим разработчиком.
3. **Структура задачи**:
   - Приоритет
   - Цель
   - Описание проблемы
   - План решения (пошаговое описание)
   - Тестирование (команды и ожидаемые результаты)
   - Критерии успеха

## Заголовок
- **Приоритет**: HIGH
- **Цель**: Создать полноценную инфраструктуру тестирования для проекта Analytics.

### Описание проблемы
- **Локация**: Проект Analytics в директории `/Analytics/`
- **Текущий код**: Отсутствуют тестовые проекты и неполная покрытие тестами
- **Проблема**: 
  - Нет проектов для юнит-тестов, интеграционных и end-to-end тестов
  - Тесты не покрывают базу данных, обработку ошибок, крайние случаи и производительность
  - Отсутствует стандартный способ настройки тестовых данных

### План решения
- **Шаг 1**: Создать структуру тестовых проектов:
  ```
  Analytics/
  ├── src/
  │   ├── ReceiptCollector.Analytics.Api/
  │   │   └── Tests/ (Юнит-тесты для API)
  │   ├── ReceiptCollector.Analytics.Infrastructure/
  │   │   └── Tests/ (Юнит-тесты для инфраструктуры)
  │   └── ReceiptCollector.Analytics.Migrations/
  │       └── Tests/ (Юнит-тесты для миграций)
  └── tests/
      ├── IntegrationTests/ (Интеграционные тесты)
      └── EndToEndTests/ (E2E тесты)
  ```

- **Шаг 2**: Добавить необходимые NuGet пакеты:
  - `Microsoft.NET.Test.Sdk`
  - `xunit` или `nunit`
  - `Moq` для моков
  - `FluentAssertions` для ассертов
  - `Respawn` для тестирования базы данных
  - `NSubstitute` как альтернатива Moq

- **Шаг 3**: Создать общую инфраструктуру тестов:
  ```csharp
  // TestBase.cs
  public abstract class TestBase : IDisposable
  {
      protected TestBase()
      {
          // Настройка общих зависимостей для тестов
      }

      public void Dispose()
      {
          // Очистка ресурсов
      }
  }
  ```

- **Шаг 4**: Добавить поддержку тестирования базы данных:
  ```csharp
  // TestDatabaseFactory.cs
  public class TestDatabaseFactory : IDisposable
  {
      private readonly RespawnGenerator _respawnGenerator;
      
      public TestDatabaseFactory()
      {
          _respawnGenerator = RespawnGenerator.New("migrations");
      }
      
      public async Task ResetDatabaseAsync(ReceiptDbContext context)
      {
          await _respawnGenerator.ResetAsync(context);
      }
  }
  ```

- **Шаг 5**: Создать билдеры тестовых данных:
  ```csharp
  // ReceiptBuilder.cs
  public class ReceiptBuilder
  {
      private readonly Receipt _receipt = new Receipt();
      
      public ReceiptBuilder WithId(string id)
      {
          _receipt.Id = id;
          return this;
      }
      
      // Другие методы билдера...
      
      public Receipt Build() => _receipt;
  }
  ```

### Тестирование
- **Команды**:
  - `dotnet test` для запуска всех тестов
  - `dotnet test --filter "Category=Unit"` для юнит-тестов
  - `dotnet test --filter "Category=Integration"` для интеграционных тестов
- **Ожидаемые результаты**:
  - Все тесты должны проходить успешно
  - Покрытие кода должно увеличиться
  - Отсутствие ошибок при настройке и очистке тестовой базы данных

### Критерии успеха
- Созданы все необходимые тестовые проекты
- Добавлены NuGet пакеты для тестирования
- Реализована общая инфраструктура тестов
- Подготовлена поддержка тестирования базы данных
- Созданы билдеры тестовых данных
- Все тесты проходят успешно
