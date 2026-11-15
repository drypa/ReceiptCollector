# Задача 01 - Репозиторий: постраничное чтение магазинов с фильтром `name OR inn`

- **Задача**: docs/plans/merchants-search.md#задача-01-репозиторий-постраничное-чтение-магазинов-с-фильтром-name-or-inn
- **Дата**: 2026-11-15
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано
В слое данных реализована аддитивная постраничная выборка магазинов с необязательным поиском. При непустом поисковом термине (после `Trim`) фильтр `name OR inn` (частичное совпадение, без учёта регистра, null-безопасно для `Inn`) применяется в запросе к БД до подсчёта `TotalCount`, сортировки и пагинации (`Skip/Take`); страница и итоговое количество считаются по одному и тому же предикату. Пустой/пробельный термин эквивалентен отсутствию фильтра, порядок детерминирован (`Name`, затем `Id`). Существующие методы репозитория, схема БД, миграции и индексы не изменялись; эндпоинт и frontend не затрагивались (задачи 02, 03).

## Изменённые файлы
- `docs/plans/merchants-search.md` - статус задачи 01 переведён в `approved` (таблица «Ход выполнения» и раздел задачи).
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/IMerchantRepository.cs` - добавлен аддитивный метод `GetPageAsync(limit, offset, searchTerm, ct)` → `(IReadOnlyList<Merchant> Items, int TotalCount)` с описанием семантики ADR-037; существующие методы не изменены.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/MerchantRepository.cs` - реализация `GetPageAsync`: нормализация термина, фильтр через `EF.Functions.ILike` по `Name` и `Inn` (null-safe), `CountAsync` и выборка страницы по одному предикату, сортировка `Name` + `Id`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/MerchantRepositoryPageTests.cs` - новый класс контейнерных тестов (PostgreSQL 16-alpine, Testcontainers) для постраничного чтения и поиска.

## Как проверено
- `cd Analytics && dotnet build tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj` - успешно (0 ошибок; присутствуют существующие предупреждения NU1902/NU1903 по зависимостям, не связаны с изменениями).
- `cd Analytics && dotnet test tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj --filter "FullyQualifiedName~MerchantRepositoryDefaultsTests"` - Passed: 8, Failed: 0 (проверка отсутствия регрессий существующего поведения репозитория).
- `cd Analytics && dotnet test tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj --filter "FullyQualifiedName~MerchantRepositoryPageTests"` - Passed: 11, Failed: 0 (реальный PostgreSQL; поиск по имени/ИНН, OR, null-ИНН, пустой/пробельный термин, пустой результат, фильтр до COUNT/SKIP/TAKE, детерминированный порядок, Trim).
- `cd Analytics && dotnet test tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj --filter "FullyQualifiedName~MerchantEndpointsTests"` - Passed: 15, Failed: 0 (регрессии не выявлены; эндпоинт ещё не переведён на новый метод — задача 02).

Независимые проверки ревьюера (@code-reviewer, раунд 1):
- `MerchantRepositoryPageTests` - Passed: 11, Failed: 0.
- `MerchantRepositoryDefaultsTests` - Passed: 8, Failed: 0.

## Итоги ревью
- Исправлено по замечаниям ревьюера: исправлений не требовалось.
- Оставлено без изменений (с обоснованием): blockers отсутствуют; замечаний, требующих доработки, нет. Предупреждения NU1902/NU1903 по транзитивным зависимостям отмечены ревьюером как не блокирующие.

## Заметки для коммита
- Рекомендуемое сообщение коммита: `Analytics: add paged merchant repository search by name or inn (ADR-037 task 01)`
- На что обратить внимание при ручной проверке: аддитивность контракта `IMerchantRepository` (существующие методы и их семантика сохранены), применение фильтра и подсчёта в запросе до сортировки/пагинации, null-безопасность сравнения по `Inn`, детерминированный порядок `Name` + `Id`, отсутствие изменений схемы БД/миграций/индексов и отсутствие изменений в эндпоинте/frontend (они в задачах 02 и 03). `GET /api/merchants` продолжит использовать `GetAllAsync` до выполнения задачи 02.
