# Задача 08 - API: `GET /api/categorization-decisions` (admin-only)

- **Задача**: docs/plans/categorization-decision-log.md#t-08-api-get-apicategorization-decisions-admin-only
- **Дата**: 2026-11-08
- **Ревью**: раундов - 1, вердикт APPROVED (блокеров нет)

## Что сделано

Реализован единственный HTTP-эндпоинт модуля журнала категоризации (ADR-033 п. 7, FR-6/FR-7): `GET /api/categorization-decisions` поверх read-сервиса T-07. Права - по эталону админских эндпоинтов: гейт `AdminAuthorization.AuthorizeAsync` идёт первым (нет пользователя - `401`, не админ - явный `403`, данные не читаются ни при каких параметрах), ошибки - текстовыми телами без ProblemDetails. Валидация параметров - на эндпоинте (репозиторий диапазон намеренно не валидирует - требование ревью T-07): `source` вне четырёх wire-имён `CategorizationDecisionSources.All` (Ordinal), `suggestedCategory` вне enum или `0`, `finalCategory` вне enum (`0` = «Не указана» допустим), непарсимые `from`/`to` (`DateTimeOffset.TryParse`, `InvariantCulture`, `RoundtripKind`), `limit` вне `1..200` (дефолт 50), `offset < 0` - всё `400`; дополнительных правил (например, `from > to`) не вводилось. Ответ `200` - массив уникальных сочетаний ровно с 11 camelCase-полями ADR-033 п. 7, `X-Total-Count` = `Total` (число уникальных сочетаний под фильтром, FR-7). Write-эндпоинтов нет (запрещено п. 9); эндпоинт зарегистрирован в `Program.cs`. Фронтенд страницы - T-11.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/CategorizationDecisions/CategorizationDecisionEndpoints.cs` (новый) - `MapCategorizationDecisionEndpoints()` (группа `/api/categorization-decisions`, тег `CategorizationDecisions`, единственный `MapGet`) и обработчик `GetDecisions`: гейт прав, валидация всех девяти параметров в порядке ADR, сборка `CategorizationDecisionQuery`, вызов `ICategorizationDecisionReadService.GetPageAsync`, заголовок `X-Total-Count` (InvariantCulture), `Results.Ok(items)`.
- `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs` - `using` + `app.MapCategorizationDecisionEndpoints();` (после `MapPrecategorizedCommodityEndpoints()`).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CategorizationDecisionEndpointsTests.cs` (новый) - 28 тестов в стиле прецедента `PrecategorizedCommodityEndpointsTests` (исполнение `IResult` на `DefaultHttpContext`): `401` без пользователя; `403` не-админу при полностью валидных параметрах + сервис не вызывается; `400` - `source` (`""`, `AI`, `existing`, `undefined`), `suggestedCategory` (`0`, `-1`, `999`), `finalCategory` (`-1`, `999`), непарсимые `from`/`to` (`""`, `not-a-date`, `2026-13-45T00:00:00Z`), `limit` (`0`, `201`), `offset` (`-1`) - везде read-сервис не вызывается; `200` + `X-Total-Count` из `Total`; пустая выдача - `200` и `0`; `finalCategory = 0` допустим и доходит до сервиса; все девять параметров транслируются в `CategorizationDecisionQuery` без потерь (включая `to` со смещением `+03:00`, сравнение по моменту); дефолты обработчика (`limit=50`, `offset=0`, `onlyMismatch=false`, фильтры `null`) доходят до сервиса; сериализация через реальный JSON-конвейер - ровно 11 camelCase-полей и `decidedAt` парсится как ISO 8601; `AcceptedByName = null` сериализуется **явным JSON-`null` при присутствующем поле** (опора T-11); регистрация - ровно один эндпоинт, метод `GET`, тег `CategorizationDecisions`.
- `docs/plans/categorization-decision-log.md` - строка статуса T-08: `pending` -> `in-progress` -> `review` -> `approved`.
- `docs/reviews/categorization-decision-log/08-read-api.md` (новый - эта сводка).

## Как проверено

- `cd Analytics && dotnet build` - Build succeeded, 0 ошибок; новых CS-предупреждений нет (только предсуществующие NU1902/NU1903 по пакетам).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CategorizationDecisionEndpoints"` - Passed: 28, Failed: 0 (150 ms) - финальный прогон после применения suggestions ревьюера.
- `cd Analytics && dotnet test` - Passed: 577, Failed: 0, Skipped: 0 (54 s). База до T-08 - 549, добавлено 28, существующие тесты не правались.
- Ревьюер (раунд 1): build 0 ошибок; фильтр эндпоинтов 27 passed (до suggestions); полный набор - зелёный, кроме `MongoReceiptBatchLoaderTests`, которые изолированно пройдены зелёными (см. «Итоги ревью» про среду).

## Итоги ревью

- **Вердикт APPROVED с первого раунда, блокеров нет.** Подтверждены: гейт прав до валидации и до обращения к сервису; `400` достижим только после авторизации; точность валидации по ADR; отсутствие write-поверхности; массив с ровно 11 полями и `X-Total-Count` по уникальным сочетаниям; трансляция всех параметров без потерь.
- **Suggestions ревьюера (не блокеры) - применены/обоснованы:**
  - добавлен тест `GetDecisions_serializes_null_accepted_by_name_as_explicit_json_null`: при `AcceptedByName = null` поле **присутствует** в ответе со значением `null` (web-дефолты System.Text.Json null не игнорируют), `acceptedById` и `acceptedByAdmin` заполнены - на это опирается фронт T-11; общий scaffolding сериализации вынесен в приватный помощник `SerializeSingleRowAsync`;
  - **Требование к T-11 (зафиксировать):** `decidedAt` сериализуется в ISO 8601 **со смещением `+00:00`**, а не со `Z` (пример в ADR-033 п. 7 приведён с `Z` - тот же момент и тот же ISO 8601; прецедент `PrecategorizedCommodityDto`). Фронт должен парсить значение как ISO 8601 со смещением и НЕ ждать строго `Z`. В тесте закреплён round-trip: разобранное из JSON значение равно `DecidedAt` строки;
  - тест дефолтов (`GetDecisions_default_parameters_reach_read_service`) оставлен как есть с обоснованием: он проверяет значения по умолчанию самого обработчика (вызов без аргументов), а не binding query-параметров ASP.NET - minimal API использует те же дефолты сигнатуры, тестировать сам binding - ответственность фреймворка; это прецедент проекта (`PrecategorizedCommodityEndpointsTests` проверяет аналогично).
- **Принятые допущения (согласованы ревью):**
  - присутствующий, но пустой параметр - невалидный (`source=""`, `from=""`, `to=""` -> `400`): параметр «присутствует» (не null) и не проходит проверку; отдельных правил не вводилось («дополнительных правил не вводить»); фронт пустые значения не отправляет (прецедент `URLSearchParams` - `if (search) ...`); зафиксировано тестами;
  - response-DTO не заводили - отдаётся `CategorizationDecisionRow`: имена свойств 1:1 покрывают список ADR-033 п. 7, camelCase даёт дефолтный конвейер minimal API, состав полей зафиксирован сериализационным тестом; прецедент - `PrecategorizedCommodityDto`;
  - текст подсказки допустимых источников в `400` собран из констант `CategorizationDecisionSources` в фиксированном порядке (обход `All` дал бы недетерминированный порядок FrozenSet).
- **Подвес полного прогона - среда, а не регрессия T-08 (доказательства, чтобы на приёмке не искали причину в задаче):**
  - осиротевшие контейнеры тестов - образ `mongo:7.0`, ровно тот, что поднимают `MongoReceiptBatchLoaderTests`/`ReceiptSynchronizationServiceTests` (`WithImage("mongo:7.0")`); древнейший висел **со статусом Exited(48) со вчерашнего дня (2 дня к моменту ревью)**, то есть появился до существования кода T-08;
  - в логе mongod - `Error setting up listener ... SocketException: Address already in use` (host-порт 27017 был занят чужим процессом на старте контейнера), `exitCode: 48`; та же ошибка наблюдалась до задачи;
  - ревьюер: эти же 6 тестов изолированно зелёные; тесты T-08 контейнеров не используют вообще (чистые unit на `DefaultHttpContext` + NSubstitute);
  - после удаления осиротевших контейнеров полный `dotnet test` - 577/577 за 54 с (включая Mongo-тесты). **Перед полным прогоном на приёмке - `docker ps -a` и убрать осиротевшие `mongo:7.0`/`testcontainers-ryuk` контейнеры.** Не трогать: `rc-mig-verify` (postgres, пользовательский) и dev-компоузовские контейнеры.

## Заметки для коммита

- Рекомендуемое сообщение коммита: `T-08 decision log read API` (в стиле `T-07 decision log read service`).
- Чек-лист файлов коммита (2 новых + 2 изменённых + эта сводка, сверить по `git status`):
  - `Analytics/src/ReceiptCollector.Analytics.Api/Modules/CategorizationDecisions/CategorizationDecisionEndpoints.cs` (новый, untracked)
  - `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CategorizationDecisionEndpointsTests.cs` (новый, untracked)
  - `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs`
  - `docs/plans/categorization-decision-log.md` (в диффе будет также строка T-07 `approved -> committed` от оркестратора - норма, пусть едет в коммите)
  - `docs/reviews/categorization-decision-log/08-read-api.md` (новый - эта сводка)
- **Не включать** предсуществующие посторонние изменения: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`, `appsettings.Development.json`, `wwwroot/index.html`.
- Внимание по состоянию индекса (агенты `git add` не выполняют): эндпоинт и `Program.cs` уже в индексе финальным состоянием; в тестовом файле и плане поверх индекса лежат нестадий правки по suggestions ревьюера (тест на `null` + round-trip `decidedAt`, статус `approved`) - перед коммитом добавить их и сводку; итог сверить по `git status`/`git diff`.
- На что обратить внимание при ручной проверке:
  1. Без cookie - `401`; под не-админом - `403` и пустое тело **даже с полностью валидными параметрами** (права проверяются до валидации и до чтения данных).
  2. Админ, все фильтры (`?onlyMismatch=true&source=ai&suggestedCategory=23&finalCategory=0&search=хлеб&from=2026-11-01T00:00:00Z&to=2026-11-07T00:00:00Z&limit=10&offset=0`) - `200`, в каждой строке ровно 11 полей (`commodityName`, `normalizedName`, `suggestionSource`, `suggestedCategoryId`, `suggestedCategoryName`, `finalCategoryId`, `finalCategoryName`, `acceptedById`, `acceptedByName`, `acceptedByAdmin`, `decidedAt`); `X-Total-Count` = число уникальных сочетаний под фильтром (обычно меньше числа событий в `categorization_decisions` - сверить с psql, как в T-07).
  3. Негативные запросы - `400` текстом: `?source=existing`, `?limit=0`, `?limit=201`, `?offset=-1`, `?suggestedCategory=0`, `?suggestedCategory=999`, `?finalCategory=999`, `?from=not-a-date`.
  4. `?finalCategory=0` - `200` (фильтр «Не указана» допустим только для итога).
  5. `decidedAt` в ответе - со смещением (`+00:00`), не `Z` - норма (см. требование к T-11).
  6. Полного прогона перед коммитом достаточно обычного `cd Analytics && dotnet test` (предварительно - чистка осиротевших контейнеров, см. «Итоги ревью»).
