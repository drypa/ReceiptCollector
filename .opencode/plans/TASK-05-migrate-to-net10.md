# Migration Plan: Analytics Project from .NET 8 to .NET 10

## Приоритет
HIGH

## Цель
Выполнить миграцию проекта ReceiptCollector.Analytics с .NET 8 на .NET 10, обеспечивая совместимость всех компонентов и зависимостей.

## Описание проблемы
Текущая версия проекта аналитики использует .NET 8, что требует обновления до .NET 10 для получения новых возможностей, улучшений производительности и поддержки. В текущем файле описана миграция, но необходимо выполнить детализацию по ключевым аспектам:

- Обновление целевой среды выполнения в проектных файлах
- Проверка совместимости пакетов EF Core 
- Обновление зависимостей Microsoft.Extensions и Testcontainers

## План решения
1. Обновить все файлы .csproj для использования target framework net10.0:
   - ReceiptCollector.Analytics.Api.csproj
   - ReceiptCollector.Analytics.Application.csproj
   - ReceiptCollector.Analytics.Infrastructure.csproj
   - ReceiptCollector.Analytics.Migrations.csproj
   - ReceiptCollector.Analytics.Api.Tests.csproj

2. Проверить и обновить пакеты EF Core:
   - Microsoft.EntityFrameworkCore 8.0.7 → проверить совместимость с .NET 10
   - Npgsql.EntityFrameworkCore.PostgreSQL 8.0.4 → проверить совместимость с .NET 10 
   - EFCore.NamingConventions 8.0.0 → проверить совместимость с .NET 10

3. Обновить зависимости Microsoft.Extensions:
   - Проверить и обновить до версий, совместимых с .NET 10 (например, 9.0.9)

4. Обновить тестовые контейнеры:
   - Testcontainer dependencies для MongoDB и PostgreSQL (3.7.0) → проверить новые версии

5. Запустить все тесты для обнаружения изменений в совместимости

## Тестирование
- Выполнить unit tests для каждого компонента проекта 
- Запустить integration tests для проверки подключения к базе данных
- Проверить работу процессов миграции и API endpoint'ов
- Убедиться, что все сервисы корректно работают после миграции

## Критерии успеха
- Все проектные файлы обновлены до .NET 10
- Все зависимости совместимы с .NET 10 
- Все unit и integration тесты проходят успешно
- База данных корректно подключается после миграции