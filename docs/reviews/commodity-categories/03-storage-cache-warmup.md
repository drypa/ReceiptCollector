# Задача 03 - Хранение: реализация репозитория, кэш снимка, hosted-прогрев

- **Задача**: docs/plans/commodity-categories.md#T-03-Хранение-реализация-репозитория-кэш-снимка-hosted-прогрев
- **Дата**: 2026-11-12
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Готов слой хранения слов категоризации (ADR-035, решение E1): EF-репозиторий таблицы
`commodity_category_keywords`, singleton-кэш неизменяемого снимка и прогрев при старте.
Один `SELECT` (без tracking) наполняет снимок; читатели берут его через `Volatile.Read`
без обращений к БД и без блокировок (NFR-1). Перечитывание - полное, под `SemaphoreSlim(1,1)`,
с атомарной подменой ссылки; при сбое БД ничего не бросается на горячий путь: кэш помечается
«грязным», старый снимок остаётся валидным, следующее чтение перечитывается (NFR-4
восстанавливается само, без рестарта). Прогрев - hosted service через `IServiceScopeFactory`,
его ошибка не валит процесс.

Рамки задачи - хранение: админского API, сервиса валидации/лимита (задача 04) и применения
подсказок в категоризации - провайдера, `IAiClient`, resolver'а и фона (задача 05) - здесь
ещё нет. Продуктовый код слова пока читает только через кэш-контракт; записи не производит.

## Изменённые файлы

Новое, Application:

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategoryKeywordCache.cs` -
  контракт кэша: `GetSnapshot()`, `RefreshAsync(ct)`; XML-doc фиксирует семантику деградации
  (сбой перечитывания не бросается на горячий путь, старый снимок остаётся валидным) и
  ограничение single-instance (ADR-035, E4 вне области).

Новое, Infrastructure:

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityCategoryKeywordRepository.cs` -
  `GetAllAsync` - один `AsNoTracking` SELECT и проекция в домен в C# с безопасным чтением
  (ADR-032: `Enum.IsDefined` + пропуск `Undefined`, плюс пропуск битого `list_type`);
  `ExistsAsync`/`CountAsync` адресны по паре «категория + список»; `AddAsync` ловит
  unique violation `23505` и возвращает `false` (detach сущности, как в `TryAddProposalAsync`);
  `DeleteAsync` - `ExecuteDeleteAsync` по `id`, `false` если строки нет.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/InMemoryCommodityCategoryKeywordCache.cs` -
  singleton-кэш: снимок через `Volatile.Read`/`Volatile.Write`, `SemaphoreSlim(1,1)` только на
  перечитывание, `volatile bool` «грязный»; `RefreshAsync` - полное перечитывание и подмена
  ссылки (сначала снимок, затем снятие флага), при ошибке `LogWarning` и флаг остаётся;
  `GetSnapshot` при «грязном» - double-check под тем же семафором. Scoped-репозиторий
  резолвится через `IServiceScopeFactory` (singleton не держит scoped-зависимость).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/CommodityCategoryKeywordCacheInitializationHostedService.cs` -
  прогрев при старте (стиль `CommodityCategoryCacheInitializationHostedService`); ошибка
  прогрева логируется и не валит процесс - кэш остаётся «грязным».

Изменено, Infrastructure:

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` -
  `AddSingleton<ICommodityCategoryKeywordCache, ...>`, `AddScoped<ICommodityCategoryKeywordRepository, ...>`,
  `AddHostedService<CommodityCategoryKeywordCacheInitializationHostedService>()` (ровно один раз).

Новые тесты (`Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/`):

- `CommodityCategoryKeywordRepositoryTests.cs` - Testcontainers Postgres, 8 тестов: снимок
  всех строк (одно слово в разных парах - разные строки); пропуск строки с неизвестным кодом
  категории; пропуск строки с битым `list_type`; адресность `Exists`/`Count` по паре;
  `AddAsync` пишет `word`/`normalized_word` как передано; дубликат в той же паре (включая
  другой регистр того же `normalized_word`) - `false` без исключения; генерация `id` при
  `Guid.Empty`; `DeleteAsync` по существующему `id` - `true`, повтор - `false`.
- `InMemoryCommodityCategoryKeywordCacheTests.cs` - 5 тестов с фейковым репозиторием:
  ленивая первая загрузка и «после прогрева репозиторий не трогается» (счётчик вызовов);
  `RefreshAsync` подменяет снимок, ссылка взятая до refresh остаётся прежней и валидной;
  «упал один раз, потом успех» (сбой refresh - старый снимок отдаётся, следующее чтение
  ретраит и успешно перечитывается); конкурентные чтения во время медленного refresh видят
  только целый старый или целый новый снимок; hosted service при недоступном репозитории
  не бросает и оставляет кэш «грязным» с ленивым прогревом.
- `CommodityCategoryKeywordCacheRegistrationTests.cs` - wiring DI: кэш - singleton,
  репозиторий - scoped, hosted-прогрев зарегистрирован ровно один раз, резолвятся.

Прочее:

- `docs/plans/commodity-categories.md` - статус задачи 03 в таблице «Ход выполнения».

## Как проверено

- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryKeywordRepository|FullyQualifiedName~CommodityCategoryKeywordCache"` -
  Passed: 14, Failed: 0.
- `cd Analytics && dotnet test` - Passed: 717, Failed: 0 (включая `ProjectDependencyTests`).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~ProjectDependencyTests"` - Passed: 4, Failed: 0.
- `cd Analytics && dotnet build` - 0 ошибок; предупреждения только предсуществующие
  (NU1902/NU1903 по пакетам), новых нет.
- Ревьюер независимо подтвердил: фильтр 14/14, полный набор 717/717, сборка чистая.

Ручная проверка (dev, из плана): после старта Analytics `SELECT count(*) FROM
commodity_category_keywords` совпадает с количеством слов в снимке - станет проверяемо
косвенно после появления счётчиков на странице задачи 07.

## Итоги ревью

- Исправлено по замечаниям ревьюера: ничего, блокеров не было (раунд 1 - APPROVED).
  Ревьюер независимо подтвердил: корректный порядок volatile-записи и «грязного» флага
  (окна «флаг чистый, снимок старый» нет), неизменяемость снимка, ровно один `SELECT`
  в `GetAllAsync`, корректный detach при `23505`, отсутствие captive dependency в DI,
  отсутствие хрупкости в конкурентном тесте.
- Оставлено без изменений (отклонения исполнителя, приняты ревьюером):
  1. `AddAsync` берёт `Id` из доменной записи (генерирует `Guid.NewGuid()` только при
     `Guid.Empty`), а не всегда сам: задаче 04 нужно вернуть `id` созданного элемента
     в теле ответа `POST` (ADR-035 п. 7), а контракт `AddAsync` возвращает только `bool`.
     Зафиксировано комментарием в коде и тестом.
  2. `RefreshAsync` не бросает вообще (включая отмену токена): буквальный смысл ADR-035 п. 6 -
     мутация уже зафиксирована и успешна независимо от результата перечитывания; сбой
     деградирует в «грязный» флаг, а не в исключение вызывающему. Снимает с задачи 04
     обязанность оборачивать вызов.
  3. Защитный `ArgumentException` в `Exists`/`Count`/`Add` для `Undefined`/неизвестного кода
     категории и неизвестного кода `KeywordListType` - по образцу `EnsureAssignableCategory`
     в `PrecategorizedCommodityRepository`; в проде недостижим (сервис валидирует первым).
  4. Синхронное перечитывание в `GetSnapshot` (`GetAwaiter().GetResult()` под семафором) -
     только деградированный путь после сбоя, как предписано планом («double-check под тем же
     семафором»); горячий путь не блокируется.
  5. При длительном простое БД «грязное» чтение логирует `LogWarning` на каждую попытку -
     принято (частота низкая, rate-limit не вводился; см. отложенный suggestion 1).
  6. Решение по битому `list_type` (замечание ревью T-02): строки, которые
     `KeywordListTypeNames.TryParse` не распознаёт (CHECK штатно их не пропускает, но
     возможна ручная правка схемы), явно пропускаются при построении снимка - так же, как
     недопустимый код категории (ADR-032). Молчаливое приведение к `Boost` запрещено;
     поведение зафиксировано тестом `GetAllAsync_skips_row_with_broken_list_type` (тест
     снимает CHECK и вставляет битую строку raw SQL) и комментарием в репозитории.
- Отложенные suggestions ревьюера (не блокеры, код в этом цикле не менялся - согласованный
  дифф не ломается; кандидаты в отдельный аддитивный коммит):
  1. `InMemoryCommodityCategoryKeywordCache.cs` (`GetSnapshot`, «грязный» путь) - при
     длительном простое БД каждое «грязное» чтение порождает серию попыток перечитывания
     (каждая - до таймаута Npgsql) и `LogWarning`. Предложен короткий cooldown под тем же
     гейтом. Обоснование отсрочки: деградированный путь редок, деградация безопасна
     (старый снимок валиден), а cooldown добавляет состояние и время устаревания -
     против NFR-4 «применяется к следующим запросам»; при росте нагрузки допустим
     предфильтр по первой букве (открытый вопрос 3 ADR-035).
  2. `RefreshAsync` - отмена по `CancellationToken` попадает в общий `catch` и логируется
     как «refresh failed». Полезно отличать `OperationCanceledException` (отдельная ветка
     `catch` без изменения семантики «не бросаем»). Обоснование отсрочки: поведение
     идентично (флаг «грязный», старый снимок), меняется только формулировка лога.
  3. `ReloadAsync` - двойная копия коллекции (`ToArray()` в кэше плюс копия в конструкторе
     снимка). Обоснование отсрочки: таблица - сотни строк, копия тривиальна; убирать её -
     значит ослаблять инвариант неизменяемости снимка (конструктор копирует намеренно, T-02).

## Заметки для коммита

- Рекомендуемое сообщение коммита:

  `feat(analytics): хранение слов категоризации - репозиторий, singleton-кэш снимка, hosted-прогрев (ADR-035, T-03)`

- В коммит НЕ включать предсуществующие локальные правки рабочего дерева (не относятся к
  задаче, это локальные dev-значения):
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
  `Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html`.
  Захватывать только файлы из раздела «Изменённые файлы» (плюс `docs/plans/commodity-categories.md`
  и эту сводку).
- На что обратить внимание при ручной проверке:
  - старт Analytics с чистой (после миграции T-01) таблицей: лог прогрева без ошибки,
    `SELECT count(*) FROM commodity_category_keywords` совпадает с числом строк сида (167);
  - остановить БД и дёрнуть чтение снимка (например, suggest при включённом ИИ): процесс жив,
    в логах `LogWarning` про reload, после возврата БД следующее чтение перечитывается;
  - в DI ровно одна регистрация `CommodityCategoryKeywordCacheInitializationHostedService`
    (закреплено `CommodityCategoryKeywordCacheRegistrationTests`).
- Следующая задача - 04 (`04-service-admin-api`) - берётся только после этого коммита
  и подтверждения человека.
