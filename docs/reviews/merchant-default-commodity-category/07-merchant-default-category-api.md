# Задача 07 - API: эндпоинты дефолта + аддитивный MerchantDto

- **Задача**: docs/plans/merchant-default-commodity-category.md#t-07-api-эндпоинты-дефолта--аддитивный-merchantdto
- **Дата**: 2026-11-06
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Готов HTTP-слой товарной категории по умолчанию магазина (ADR-032 п. 6) — то, через что
UI (T-09/T-10) будет настраивать дефолт и дожимать историю. Два новых маршрута в существующей
группе `/api/merchants`: `PUT .../default-commodity-category` (установка/сброс; тело
`{ "categoryId": int | null }`, `null` = сброс; порядок проверок 401 → явный 403 → 404 →
валидация кода справочника → сохранение существующим repo-путём редактирования merchant по
ADR 011) и `POST .../default-commodity-category/apply` (дожим через сервис T-06; исходы
`NotFound → 404`, `DefaultNotConfigured → 400` текстом, `Applied → 200 { updated }`, включая
`updated: 0` — идемпотентность). `MerchantDto` расширен аддитивными полями
`DefaultCommodityCategoryId/DefaultCommodityCategoryName` (со значениями по умолчанию —
все существующие построения компилируются без правок); список `GET /api/merchants` заполняет
новые поля. 403 — явно `Results.StatusCode(403)` через общий `AdminAuthorization` (ADR-028
п. 10), дефект `Results.Forbid()` в новых хендлах не воспроизведён и покрыт тестами
исполнением `IResult`.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Receipts/Models/MerchantDto.cs` — аддитивные поля `DefaultCommodityCategoryId int? = null`, `DefaultCommodityCategoryName string? = null` + XML-doc. (В плане указан путь `Modules/Merchants/Models/` — фактически DTO живёт здесь, помечен ADR-032 как «переиспользуемый»; переезд вне объёма задачи.)
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Merchants/MerchantEndpoints.cs` — регистрация двух маршрутов; хендлеры `UpdateDefaultCommodityCategory` / `ApplyDefaultCommodityCategory` (`public static async Task<IResult>`, XML-doc с отсылками ADR-032); guard `request is null → 400` (по замечанию ревью); проекция `ToDto` для списка; request/response-записи `UpdateMerchantDefaultCategoryRequest/Response`, `ApplyMerchantDefaultCategoryResponse`. Существующие маршруты и хендлеры не изменены.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/MerchantDefaultCategoryEndpointsTests.cs` — новый, 20 тестов: PUT 401 / 403 (исполнением `IResult` — именно 403, не 500) / 404 / 400 (код 0, −1, 999) / 200 set / 200 reset / 400 при null-теле (+ приоритет 401 над валидацией тела) / `{}`-десериализация = сброс; Apply 401 / 403 / 404 / 400 DefaultNotConfigured (точный текст) / 200 `updated: 0` и `updated: N` (+ проброс `adminUserId` сервису); `GET /api/merchants` — значения новых полей и camelCase wire-имена в JSON (в т.ч. `null`); регистрация обоих маршрутов (метод + путь).
- `docs/plans/merchant-default-commodity-category.md` — чек-лист T-07 отмечен, статус строки 07 → approved.

## Как проверено

- `cd Analytics && dotnet build` — Build succeeded, 0 errors (30 warning — существующие NU19xx; новых CS-предупреждений нет).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~MerchantDefaultCategoryEndpoints"` — Passed: 20, Failed: 0.
- `cd Analytics && dotnet test` — Passed: 436, Failed: 0 (базлайн 433 + 3 новых теста после замечаний ревью; существующие тесты не правились).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~ProjectDependencyTests"` — Passed: 4, Failed: 0.

## Итоги ревью

- Исправлено по замечаниям ревьюера (раунд 1, дешёвые): guard `request is null → 400
  "Request body is required."` в PUT-хендлере (JSON-literal `null` вместо объекта давал NRE→500);
  guard размещён после 404, чтобы порядок 401 → 403 → 404 → 400 не менялся; добавлены тесты
  «null-тело → 400», «нет пользователя + null-тело → 401» и фиксация `{}` = сброс на границе
  десериализации. У apply-хендлера `[FromBody]`-параметра нет — замечание к нему неприменимо.
- Оставлено без изменений (согласовано с ревьюером):
  - тело `{}` без поля `categoryId` трактуется как сброс — согласовано с «`null` = сброс»
    (оба читаются как «категорию не задали»); зафиксировано тестом;
  - полнота покрытий маппинга исходов apply — через `_ =>` (exhaustiveness по прецеденту
    `PrecategorizedCommodityEndpoints.Confirm`): третий исход — единственный оставшийся,
    новые значения enum потребуют правки тестов сервиса и эндпоинта одновременно.

## Заметки для коммита и T-10

**Состав коммита (только эти файлы):**

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Receipts/Models/MerchantDto.cs`
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Merchants/MerchantEndpoints.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/MerchantDefaultCategoryEndpointsTests.cs`
- `docs/plans/merchant-default-commodity-category.md`

**НЕ включать в коммит:** `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
`appsettings.Development.json` — изменены в рабочем дереве до начала задачи, к T-07 отношения
не имеют; в базовом `appsettings.json` содержится реальный Telegram ID `136871539` — его
попадание в историю недопустимо.

**GAP ДЛЯ T-10 (важно):** `ReceiptReadService.cs` (строки 38, 91, 123) строит `MerchantDto`
пятиаргументно — в `GET /api/receipts/{id}` и в списках чеков поля
`defaultCommodityCategoryId/Name` будут `null`. Чек-лист T-10 читает
`receipt.merchant.defaultCommodityCategoryName` — заполнение нужно расширить в рамках T-10
(минимум `GetByIdAsync`, строка 91; для единообразия — все три места). Отображаемое имя даёт
C#-словарь `CommodityCategoryHelper.GetDisplayName` — в SQL-проекцию (`Select` до
`ToListAsync`) не переносится: имя вычислять после materialization (код колонки
`MerchantEntity.DefaultCommodityCategoryId` спроецируется, имя — на клиенте проекции).

**Рекомендуемое сообщение коммита:**
`T-07: API merchant default commodity category (PUT + POST apply, additive MerchantDto)`

**На что обратить внимание при ручной проверке (после миграции T-01 на dev):**

- `curl -i -X PUT http://localhost/api/merchants/<id>/default-commodity-category -H 'Content-Type: application/json' -d '{"categoryId":5}'` → `200 {"categoryId":5,"categoryName":"Аптека"}`;
  `-d '{"categoryId":null}'` или `-d '{}'` → `200 {"categoryId":null,"categoryName":null}`;
  `-d 'null'` → `400 "Request body is required."` (не 500); `-d '{"categoryId":0}'` → `400`;
- не-админ (прямой вызов с cookie обычного пользователя) → `403`, не `500` — это ключевая
  проверка явного `Results.StatusCode(403)`;
- `GET /api/merchants` — у магазина с дефолтом новые поля заполнены, без дефолта — `null`;
- `POST .../default-commodity-category/apply` по магазину без дефолта → `400` с текстом
  «Default commodity category is not set for this merchant.»; повторный apply → `200 {"updated":0}`.
