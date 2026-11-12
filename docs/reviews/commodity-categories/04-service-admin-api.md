# Задача 04 - Сервис ключевых слов + админский API

- **Задача**: docs/plans/commodity-categories.md#T-04-сервис-ключевых-слов-админский-api
- **Дата**: 2026-11-12
- **Ревью**: раундов - 2, вердикт APPROVED

## Что сделано

Готов админский контур слов категоризации (ADR-035 п. 6-7, FR-3/FR-4/FR-5/FR-10): сервис
`CommodityCategoryKeywordService` с порядком защитных проверок ровно по таблице ошибок
ADR-035 п. 7 (категория → `listType` → слово после trim 2-64 + нормализованная форма →
дубликат → лимит 200 на пару) и три эндпоинта под `AdminAuthorization`:
`GET /api/commodity-category-keywords` (полный снимок по всем категориям реестра без
`Undefined`, включая пустые списки), `POST` (ответ `200` - не `201` - с телом
`{ id, categoryId, listType, word }`) и `DELETE /{id:guid}` (`204`, отсутствие строки -
`404 word not found.`). Тексты и коды ошибок совпадают с ADR-035 п. 7 символ в символ;
`401` без сессии, `403` не-админу. Инвалидация in-memory кэша (`RefreshAsync`) выполняется
строго после commit и с `CancellationToken.None` - сбой перечитывания или обрыв клиентского
соединения не меняют исход зафиксированной мутации (кэш остаётся «грязным», NFR-4).

Рамки задачи - сервис и API: эндпоинты для фронтенда страницы «Категории товаров» появятся
в задаче 07; применение подсказок в категоризации (провайдер, `IAiClient`, resolver, фон) -
в задачах 05/06. Существующие suggest/categories-эндпоинты и их DTO не изменены (NFR-3).

## Изменённые файлы

Новое, Application:

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategoryKeywordService.cs` -
  контракт `GetAllAsync`/`AddAsync`/`DeleteAsync`; XML-doc фиксирует порядок проверок,
  что проверка слова включает длину после trim и нормализованную форму, и что тексты
  `ArgumentException` в HTTP-ответ не уходят (контрактные `400` формирует эндпоинт).
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CommodityCategoryKeywordModels.cs` -
  `CategoryKeywordsDto`, исходы `AddKeywordOutcome { Added, Duplicate, LimitReached }` и
  `DeleteKeywordOutcome { Deleted, NotFound }`, `AddKeywordResult` с созданным элементом
  для тела ответа `POST`.

Новое, Infrastructure:

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategoryKeywordService.cs` -
  сервис: порядок проверок по ADR-035 п. 7 (защитные `ArgumentException` дублируют валидацию
  эндпоинта - прецедент `PrecategorizedCommodityService.UpdateCategoryAsync`); `Id`
  генерируется до вставки (отклонение задачи 03 - репозиторий берёт `Id` из доменной записи);
  `false` из репозитория (гонка, `23505`) → `Duplicate`, а не `500`; предикат
  `NormalizedWordFitsColumn` - страховка от `normalized_word` длиннее `varchar(64)`
  (ошибка `22001` репозиторием не перехватывается); инвалидация кэша после commit в
  защитном `catch` с `CancellationToken.None`.

Новое, Api:

- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/CommodityCategoryKeywords/CommodityCategoryKeywordEndpoints.cs` -
  `MapGroup("/api/commodity-category-keywords").WithTags("CommodityCategoryKeywords")`,
  `MapGet("")`/`MapPost("")`/`MapDelete("/{id:guid}")`, `AdminAuthorization.AuthorizeAsync`
  в каждом; валидация `400` с зеркальной проверкой нормализованной длины; switch исходов
  `POST` с явной веткой `Added` и `_ => throw` для неизвестного исхода.

Изменено (минимальный дифф, NFR-3):

- `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs` - 2 строки: `using` +
  `app.MapCommodityCategoryKeywordEndpoints();`.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` -
  3 строки: `AddScoped<ICommodityCategoryKeywordService, CommodityCategoryKeywordService>()`.

Новые тесты (всего 45: 19 сервис / 25 эндпоинты / 1 DI):

- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityCategoryKeywordServiceTests.cs` -
  19 тестов: трим и регистр (`Word`/`NormalizedWord`), дубликат case-insensitive и гонка →
  `Duplicate`, лимит 199/200 по паре, `Deleted`/`NotFound`, `RefreshAsync` только после
  успешных мутаций, сбой refresh не меняет исход, runtime-independent тест на «İ» (U+0130)
  и границы предиката `NormalizedWordFitsColumn` (64/65, суррогатные пары).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategoryKeywordEndpointsTests.cs` -
  25 тестов: `401`/`403` на всех трёх эндпоинтах, снимок `GET` (все категории без
  `Undefined`, пустые списки, `Group`, отсутствие `X-Total-Count` при исполнении `IResult`),
  все `400`/`409`/`200`/`204`/`404` с контрактными текстами, тест регистрации - три пары
  «метод + шаблон» через `HttpMethodMetadata`, runtime-independent тест на «İ».
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityCategoryKeywordServiceRegistrationTests.cs` -
  wiring DI: сервис scoped, резолвится.

Прочее:

- `docs/plans/commodity-categories.md` - статус задачи 04 в таблице «Ход выполнения».

## Как проверено

- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryKeyword"` -
  Passed: 84, Failed: 0 (45 тестов задачи + 39 тестов задачи 03 по тому же префиксу).
- По классам: `CommodityCategoryKeywordServiceTests` - 19, `CommodityCategoryKeywordEndpointsTests` - 25,
  `CommodityCategoryKeywordServiceRegistrationTests` - 1.
- `cd Analytics && dotnet test` - Passed: 762, Failed: 0 (включая `ProjectDependencyTests`).
- `cd Analytics && dotnet build` - 0 ошибок; предупреждения только предсуществующие
  (NU1902/NU1903 по пакетам), новых нет.
- Ревьюер независимо подтвердил: раунд 1 - 759/759, фильтр 81/81, новые классы 42/42;
  раунд 2 - 762/762, фильтр 84/84, по классам 19/25/1.

Ручная curl-приёмка из плана НЕ выполнена автоматизацией: автотесты уровня хендлеров
(прямой вызов методов эндпоинтов + исполнение `IResult` на `DefaultHttpContext`) не покрывают
реальный pipeline - `UserAuthCookieMiddleware` (фактический `401` без сессии), ограничение
маршрута `:guid` и SPA-fallback при не-GUID `id` в `DELETE`. Это зона ручного ревью
человеком: под админом проверить `GET` (снимок), `POST` (добавление, повтор - `409`),
`DELETE` (`204`, повтор - `404`), без сессии - `401`, не-админ - `403`.

## Итоги ревью

- Раунд 1 - APPROVED без блокеров, с 6 дешёвыми suggestions, внедрено (раунд 2):
  1. гард длины нормализованной формы (`NormalizedWordFitsColumn` + гард «3a» в сервисе,
     зеркальная проверка в эндпоинте, `400` тем же текстом) - страховка от `22001`,
     который репозиторий не перехватывает (только `23505`);
  2. явная ветка `AddKeywordOutcome.Added` в switch `POST` + `_ => throw
     InvalidOperationException` для нового исхода enum;
  3. post-commit `RefreshAsync` с `CancellationToken.None` - инвалидация не зависит от
     обрыва клиентского соединения;
  4. усилен тест регистрации (три пары «метод + шаблон»; у `GET`/`POST` фактический
     `RoutePattern.RawText` - с завершающим слэшем, артефакт конкатенации группы и пустого
     подмаршрута, сравнение с `TrimEnd('/')`) + проверка отсутствия `X-Total-Count` на `GET`;
  5. XML-doc: тексты защитных `ArgumentException` в HTTP-ответ не уходят;
  6. исправлена разбивка по тестам в отчётах: факт 17/24/1 = 42 (раунд 1), после раунда 2 -
     19/25/1 = 45.
- Раунд 2 - APPROVED, блокеров и регрессий нет. Уточнение ревьюера по пункту 1: на
  .NET 10.0.7 (Unix, ICU) `U+0130` даёт 1 руну, расширяющих кодовых точек во всём Unicode -
  0; `ToLower/ToUpper` в .NET используют простые 1:1 регистропреобразования, поэтому
  расширение неправдоподобно и для Windows/NLS (находка раунда 1 опиралась на семантику
  Python/Java с full case mappings). По решению человека поправлены только формулировки
  комментариев: гард оставлен как дешёвая страховка на случай смены нормализации
  (например, full case folding) или сужения колонки; поведение не менялось.
- Отложенные follow-up'ы (не блокеры, код в этом цикле не менялся):
  1. асимметрия `DeleteKeywordOutcome` в `DELETE`-эндпоинте: нет явной ветки `Deleted` +
     `throw` для неизвестного исхода (сейчас `_ => Results.NoContent()`) - зафиксированный
     follow-up;
  2. дублирование предиката длины нормализованной формы в Api и Infrastructure - перенести
     в `CommodityCategoryKeywordRules` (Domain) при ближайшей правке Domain;
  3. опциональный короткий бюджет таймаута на post-commit инвалидацию кэша (чтобы
     зависшая БД не удерживала запрос админа);
  4. cooldown ленивых перечитываний «грязного» кэша (перенесён из задачи 03).

## Заметки для коммита

- Рекомендуемое сообщение коммита:

  `feat(analytics): сервис слов категоризации и админский API - снимок, добавление/удаление с валидацией и инвалидацией кэша (ADR-035, T-04)`

- В коммит НЕ включать предсуществующие локальные правки рабочего дерева (не относятся к
  задаче, это локальные dev-значения):
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
  `Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html`.
  Захватывать только файлы из раздела «Изменённые файлы» (плюс `docs/plans/commodity-categories.md`
  и эту сводку).
- На что обратить внимание при ручной проверке:
  - curl-приёмка под админом (см. «Как проверено»): `GET`/`POST`/`DELETE`, `401` без сессии,
    `403` не-админу - через реальный pipeline с `UserAuthCookieMiddleware`;
  - `DELETE` с не-GUID `id`: как ведёт себя связка `:guid` + SPA-fallback (404 маршрута или
    отдача SPA) - зафиксировать фактическое поведение;
  - после добавления/удаления слова подсказки suggest начинают видеть изменение без
    рестарта (инвалидация кэша после commit);
  - в OpenAPI-разметке появилась группа `CommodityCategoryKeywords`.
- Следующая задача - 05 (`05-hints-pipeline`) - берётся только после этого коммита
  и подтверждения человека.
