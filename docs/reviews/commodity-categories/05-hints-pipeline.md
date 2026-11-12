# Задача 05 - Доставка подсказок: провайдер, смена сигнатуры `IAiClient`, resolver и фон

- **Задача**: docs/plans/commodity-categories.md#t-05-доставка-подсказок-провайдер-смена-сигнатуры-iaiclient-resolver-и-фон
- **Дата**: 2026-11-12
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Подсказки по ключевым словам (ADR-035, решения C1/D1, FR-6/FR-8) теперь считаются ровно одним
провайдером `CommodityCategoryHintProvider` (снимок singleton-кэша + чистый матчер Domain) и
доходят до `IAiClient` из обеих точек применения: ручного suggest (чек и страница «Товары»,
через `CommodityCategoryResolver`, один расчёт на уникальное очищенное имя - дедупликация
ADR-034 сохранена) и фоновой прекатегоризации (`PrecategorizationBackgroundService`, по тому
же очищенному имени, что и вызов ИИ).

В промт подсказки пока не попадают: `OpenAiCompatibleAiClient` принимает параметр `hints` и
валидирует его, но тело запроса не меняет - рендеринг блока подсказок и удаление хардкода
«Разграничения схожих категорий» это задача 06. Поэтому поведение категоризации для
пользователя сейчас не изменилось: при пустых подсказках промт байт-в-байт прежний
(существующий тест промта зелёный).

Сигнатура `IAiClient.SuggestCategoryAsync` получила обязательный параметр
`IReadOnlyCollection<CommodityCategoryKeywordHint> hints` перед `CancellationToken` (без
значения по умолчанию, D1): пустая коллекция означает «совпадений нет», `null` не допускается.
Именно эта смена сигнатуры вызвала механическую правку пяти файлов тестов (добавление пустой
коллекции/ matcher'а в вызовы, утверждения не менялись), и теперь компилятор физически не
позволит новому call site забыть подсказки.

## Изменённые файлы

Новое, Application:

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategoryHintProvider.cs` -
  контракт `GetHints(normalizedName) -> IReadOnlyList<CommodityCategoryKeywordHint>`: единственная
  точка доставки подсказок обоим вызывающим (ADR-035 п. 4.2).
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CommodityCategoryKeywordModels.cs` -
  добавлен `sealed record CommodityCategoryKeywordHint(CommodityCategory Category, KeywordListType
  ListType, string Word)` в существующий файл моделей задачи 04; XML-doc фиксирует: подсказки -
  данные запроса, решение остаётся за моделью, порядок (exclude первыми) задаёт матчер.

Новое, Infrastructure:

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategoryHintProvider.cs` -
  `internal sealed` реализация: `cache.GetSnapshot()` + `CategoryKeywordMatcher.GetHints(...)`;
  ничего не знает про БД и про промт; порядок из матчера не пересортировывает.

Изменённое, production:

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/IAiClient.cs` -
  обязательный параметр `hints` перед `CancellationToken` (D1), XML-doc.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/AI/OpenAiCompatibleAiClient.cs` -
  новая сигнатура; `hints` принимается и валидируется (`ArgumentNullException.ThrowIfNull`),
  на тело запроса пока не влияет, явный комментарий «рендеринг блока подсказок - T-06»;
  `BuildPayload` и хардкод «Разграничения схожих категорий» не тронуты (удаляются в 06).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs` -
  обязательный `ICommodityCategoryHintProvider` в конструкторе; в `SuggestByAiAsync`:
  `GetHints(item.Key)` -> `SuggestCategoryAsync(item.Key, CategoryNames, hints, ct)`; Debug-лог
  количества подсказок (без текстов слов, ADR-035 п. 9). Приоритеты
  `existing -> merchant-default -> cache -> precategorized -> ai`, коды ответов, `503`/`aiAvailable`
  и бюджет `SuggestTimeout` без изменений.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/PrecategorizationBackgroundService.cs` -
  `ICommodityCategoryHintProvider` резолвится из scope'а сессии и передаётся в
  `ProcessCandidateAsync`; `GetHints(normalized)` + Debug-лог перед вызовом ИИ (снаружи `try`,
  чтобы счётчик `AiCalls` в `finally` считал ровно вызовы ИИ). Окно, пауза, `BatchLimit`,
  курсор, проверки имени без изменений.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` -
  `AddScoped<ICommodityCategoryHintProvider, CommodityCategoryHintProvider>()`.

Изменённое, тесты:

- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/OpenAiCompatibleAiClientTests.cs` -
  во все 14 вызовов добавлена пустая коллекция подсказок; утверждения (включая тест хардкода
  `SuggestCategoryAsync_prompt_contains_new_category_heuristics`) не изменены.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategorizationServiceTests.cs` и
  `CommodityBatchCategorizationServiceTests.cs` - механическая правка всех call site
  (`Arg.Any<IReadOnlyCollection<CommodityCategoryKeywordHint>>()` в стабах/проверках,
  4-аргументные `DidNotReceiveWithAnyArgs`); фабрики resolver'а передают подменённый провайдер
  с пустой коллекцией; утверждения не изменены.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategoryResolverTests.cs` -
  фабрика использует реальный `CommodityCategoryHintProvider` поверх замоканного
  `ICommodityCategoryKeywordCache` (пустой снимок по умолчанию); новые тесты: подсказка уходит по
  нормализованному имени (снимок `DriedFruits/boost/изюм` -> для «Изюм 250 г» захват аргумента),
  пустая (не `null`) коллекция при отсутствии совпадений, дедупликация «одна позиция ИИ на
  очищенное имя с тем же набором подсказок»; регресс приорететов - существующие тесты.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizationBackgroundServiceTests.cs` -
  `FakeAiClient` с новой сигнатурой и записью `CallHints`; новый `FakeHintProvider` (запись
  запросов) зарегистрирован в Harness; новый тест: `*3455614 Курага` -> подсказки запрошены по
  `курага` и дошли до `IAiClient`; `Undefined`/пауза/окно/лимит - регресс без изменений.
- `docs/plans/commodity-categories.md` - статус задачи 05 в таблице «Ход выполнения».

## Как проверено

- `cd Analytics && dotnet build` - Build succeeded, 0 ошибок (NU19xx - предсуществующие
  warnings уязвимостей пакетов).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryResolver|FullyQualifiedName~Precategorization"` -
  Passed: 73, Failed: 0.
- `cd Analytics && dotnet test` - Passed: 766, Failed: 0, Skipped: 0 (включая
  `ProjectDependencyTests` 4/4 и существующие тесты промта).
- Самопроверка: grep по `src` - `CategoryKeywordMatcher` вызывается ровно в одном месте
  (`CommodityCategoryHintProvider.cs:28`); ни одного call site `SuggestCategoryAsync` без
  `hints` (гарантия компилятора - обязательный параметр).
- Визуально содержимое промта в этой задаче не проверяется и не менялось - визуальная приёмка
  промта (блок подсказок, удаление хардкода) только в задачах 06 и 08.

## Итоги ревью

- Ревьюер подтвердил: `BuildPayload` не затронут, `hints` в тело запроса не попадает; матчер -
  ровно в одном месте; `GetHints` один раз на уникальное нормализованное имя после `GroupBy`;
  приоритеты, `503`/`aiAvailable`, `SuggestTimeout`, окно/пауза/`BatchLimit`/курсор фона не
  тронуты; captive dependency нет (фон резолвит scoped-провайдер из `using var scope` сессии);
  в логах только `{HintCount}` и название товара; новые тесты реально ловили бы регресс
  (пустая коллекция вместо подсказок, сырое имя вместо очищенного).
- Исправлено по замечаниям ревьюера: замечаний-блокеров не было (1 раунд).
- Оставлено без изменений (принятые отклонения разработчика, подтверждены ревьюером):
  1. `hintProvider` в конструкторе resolver'а - обязательный параметр (не `null`-по-умолчанию,
     в отличие от `merchantDefaultProvider` из ADR-032): план требует безусловную доставку
     подсказок; тестовые фабрики обновлены механически.
  2. Debug-логи размещены в двух точках применения (resolver и фон), а не в ИИ-клиенте: клиент
     в этой задаче не должен ничего делать с `hints`; текстов слов в логах нет (ADR-035 п. 9).
  3. `ArgumentNullException.ThrowIfNull(hints)` в клиенте - реализация «`null` не допускается».
  4. В `CommodityCategorizationServiceTests`/`CommodityBatchCategorizationServiceTests`
     провайдер - заглушка с пустой коллекцией (эти тесты не про подсказки); в
     `CommodityCategoryResolverTests` - реальный провайдер, чтобы тесты проверяли связку
     снимок -> матчер -> аргумент.
  5. `GetHints` в фоне вынесен перед `try`, чтобы `finally`-счётчик `AiCalls` учитывал ровно
     вызовы ИИ (пауза `IntervalBetweenCalls` не «съедается» сбоем провайдера).
- Отложено как nit'ы ревьюера (код в этом цикле не меняется, follow-up):
  1. `PrecategorizationBackgroundService.cs:392-395` - Debug-лог пишет `normalized`, тогда как
     остальные логи метода используют сырое `name` (ADR-034 FR-4); для единообразия
     сопоставления логов логичнее `name`.
  2. `CommodityCategoryResolver.cs:352` - `GetHints` вызывается после взятия семафора; можно
     вынести до `WaitAsync`, чтобы семафор удерживался ровно на время HTTP-вызова.

## Заметки для коммита

- Рекомендуемое сообщение коммита (стиль предыдущих задач `t-01`...`t-04`):
  `t-05 hints pipeline: ICommodityCategoryHintProvider, обязательный hints в IAiClient, resolver и фон`
- В коммит НЕ включать предсуществующие локальные правки рабочего дерева:
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
  `Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` - это локальные настройки
  окружения (строки подключения Mongo, локальный AI-эндпоинт, id администратора), они не
  относятся к задаче.
- Ручная проверка этой задачи ограничена (по плану T-05): в Debug-логе на каждый вызов ИИ
  видно только количество совпавших подсказок; визуально промт проверяется в 06/08.
- После коммита следующей берётся задача 06 (промт: рендеринг блока подсказок, удаление
  хардкода и теста хардкода - жёсткая связка «блок и тест удаляются только вместе»).
