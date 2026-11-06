# Задача 06 - Дожим: контракт и сервис IMerchantDefaultCategoryService.ApplyExistingAsync

- **Задача**: docs/plans/merchant-default-commodity-category.md#t-06-дожим-контракт-и-сервис-imerchantdefaultcategoryservice
- **Дата**: 2026-11-06
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Готов сервис дожима исторических позиций магазина категорией по умолчанию (кнопка
«Категоризировать существующие», ADR-032 п. 4, E1/FR-M3) — уровень контракта и бизнес-логики
без HTTP (эндпоинты — T-07). Форма результата — outcome-объект
`MerchantDefaultApplyResult(MerchantDefaultApplyOutcome Outcome, int UpdatedCount)` с исходами
`NotFound` / `DefaultNotConfigured` / `Applied` по amend ADR-032 (открытый вопрос 2); ожидаемые
бизнес-состояния возвращаются значением, исключения инфраструктуры пробрасываются. Логика —
три шага ADR: загрузка магазина (`null` → NotFound), проверка настроенного дефолта
(включая невалидный код справочника, безопасно прочитанный как null → DefaultNotConfigured),
затем единственная репозиторная операция `ApplyMerchantDefaultForMerchantAsync` (транзакция,
предикат «без категории», `IgnoreQueryFilters()` E1, `TryAdd` в кэш после commit — всё внутри,
не дублируется). `Applied` с 0 — успех (идемпотентность). Сервис зарегистрирован в DI как Scoped
со wiring-тестом.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Merchants/Models/MerchantDefaultCategoryModels.cs` — новый: outcome-enum + результат-record (форма по amend ADR-032, прецедент `ConfirmPrecategorizedResult`).
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Merchants/Contracts/IMerchantDefaultCategoryService.cs` — новый: контракт `ApplyExistingAsync(adminUserId, merchantId, ct)`; XML-doc фиксирует границы (права — эндпоинт, аудит-параметр, без собственного `IgnoreQueryFilters`, без записи в `precategorized_commodities`).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/MerchantDefaultCategoryService.cs` — новый: реализация `internal sealed` (стиль `PrecategorizedCommodityService`), три шага + `LogInformation` на успех, `LogDebug` на NotFound/DefaultNotConfigured (аудит неудачных нажатий, добавлено по замечанию ревью).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` — регистрация Scoped рядом с ADR-032 блоком.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/MerchantDefaultCategoryServiceTests.cs` — новый: 6 тестов (NSubstitute) — NotFound, DefaultNotConfigured (в т.ч. граница «невалидный код → null»), Applied с пробросом счётчика, Applied/0 = успех, проброс сбоя репозитория без конвертации в исход.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/MerchantDefaultCategoryServiceRegistrationTests.cs` — новый: wiring DI (Scoped, resolve в скоупе), прецедент T-03.
- `docs/plans/merchant-default-commodity-category.md` — чек-лист T-06 отмечен, статус строки 06 → approved.

## Как проверено

- `cd Analytics && dotnet build` — Build succeeded, 0 errors (30 warning — существующие NU19xx).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~MerchantDefaultCategoryService"` — Passed: 8, Failed: 0.
- `cd Analytics && dotnet test` — Passed: 416, Failed: 0 (базлайн 416 после правок по замечаниям не изменился; существующие тесты не правились; `Architecture/ProjectDependencyTests` зелёные — Application не ссылается на Infrastructure).

## Итоги ревью

- Исправлено по замечаниям ревьюера (раунд 1, дешёвые): добавлен `LogDebug` с `adminUserId`/`merchantId`
  для исходов NotFound и DefaultNotConfigured (админский аудит неудачных нажатий; коды ответа даёт
  эндпоинт T-07). В комментарий теста-границы «невалидный код читается как null» добавлена ссылка:
  фактическая проверка safe-read — в MapToDomain-тестах T-02 (`MerchantEntityMappingTests`).
- Оставлено без изменений (с обоснованием): сам тест «невалидный код → DefaultNotConfigured» —
  зафиксирован как документирование границы safe-read на уровне сервиса (на этом уровне домен
  неотличим от явного null; дубль намеренный, согласован с ревьюером).
- Допущения (согласованы ревью): outcome/record в `Merchants/Models/` по конвенции Application;
  `adminUserId` — аудит-параметр; wiring DI-тест сверх списка задачи по прецеденту T-03;
  «одна транзакция» не дублируется в сервисе — ADR-032 п. 4 относит её к репозиторной операции.

## Заметки для коммита

- Рекомендуемое сообщение коммита:
  `feat(analytics): сервис дожима merchant-default (T-06) — контракт, MerchantDefaultCategoryService, DI`
- Состав коммита: 3 новых src-файла (`MerchantDefaultCategoryModels.cs`,
  `IMerchantDefaultCategoryService.cs`, `MerchantDefaultCategoryService.cs`),
  `DependencyInjectionExtensions.cs`, 2 новых тест-файла
  (`MerchantDefaultCategoryServiceTests.cs`, `MerchantDefaultCategoryServiceRegistrationTests.cs`),
  `docs/plans/merchant-default-commodity-category.md`.
- НЕ включать: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
  `appsettings.Development.json` — попутные локальные правки стенда пользователя
  (LM Studio endpoint, dev-креды Mongo, id администратора), к T-06 отношения не имеют.
- На что обратить внимание при ручной проверке: T-07 будет маппировать исходы
  `NotFound → 404`, `DefaultNotConfigured → 400` текстом, `Applied → 200 { updated }`
  (в т.ч. 0); порядок прав 401/403 остаётся на эндпоинте.
