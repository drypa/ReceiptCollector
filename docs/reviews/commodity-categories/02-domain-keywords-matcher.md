# Задача 02 - Домен: тип списка, правила слова, матчер, контракт репозитория

- **Задача**: docs/plans/commodity-categories.md#T-02-Домен-тип-списка-правила-слова-матчер-контракт-репозитория
- **Дата**: 2026-11-12
- **Ревью**: раундов - 2, вердикт APPROVED

## Что сделано

В Domain появились чистые правила слов категоризации: тип списка с wire-именами
`boost`/`exclude`, запись слова, неизменяемый снимок всех слов, валидация/нормализация слова
и единственная функция матчинга с детерминированным порядком подсказок, а также контракт
репозитория. Обращений к БД, DI и промту в этом слое нет: матчер — статическая функция,
которая тестируется без базы. Домен готов к тому, чтобы задача 03 реализовала репозиторий,
кэш и прогрев, а задача 04 — сервис и админский API.

## Изменённые файлы

Новые файлы Domain (`Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/`):

- `KeywordListType.cs` - `enum KeywordListType { Boost, Exclude }` и `KeywordListTypeNames`
  (wire-имена `boost`/`exclude`, `ToWireName`, `TryParse`).
- `CommodityCategoryKeyword.cs` - запись слова `(Id, Category, ListType, Word, NormalizedWord)`.
- `CommodityCategoryKeywordSnapshot.cs` - неизменяемый снимок слов; коллекция копируется в
  конструкторе, есть пустой снимок `Empty` (размещён в Domain, чтобы матчер не зависел от
  Application).
- `CommodityCategoryKeywordRules.cs` - константы `MinWordLength = 2`, `MaxWordLength = 64`,
  `MaxWordsPerList = 200`, `NormalizeWord` (trim + lowercase invariant + схлопывание пробелов),
  `IsValidWord` (длина после trim в символах/кодовых точках).
- `CategoryKeywordMatcher.cs` - `static GetHints(snapshot, normalizedProductName)`: пустое,
  пробельное или безбуквенное название дает пустой список; совпадение - подстрока по уже
  нижнему регистру, без границ слов; порядок exclude → boost, внутри - код категории asc,
  затем `normalized_word` (ordinal) asc; результат не усекается.
- `ICommodityCategoryKeywordRepository.cs` - контракт `GetAllAsync`, `ExistsAsync`,
  `CountAsync`, `AddAsync` (bool, `false` при конфликте уникальности), `DeleteAsync` (bool,
  `false` если строки нет).

Новые тесты (`Analytics/tests/ReceiptCollector.Analytics.Api.Tests/`):

- `CommodityCategoryKeywordRulesTests.cs` - нормализация, идемпотентность, длины 1/2/64/65,
  пустое/пробельное слово, спецсимволы и эмодзи, единицы измерения длины, константы.
- `CategoryKeywordMatcherTests.cs` - регистр сохранённого слова, `соль` в `фасоль`, пустое и
  цифровое название, boost двух категорий, boost + exclude одной категории (exclude первым),
  детерминированный порядок на перевёрнутом снимке, 200 слов х 2 категории без усечения,
  пустой снимок, защита от пустого `normalized_word`, изоляция снимка от исходного списка,
  отсутствие повторной нормализации названия.
- `KeywordListTypeNamesTests.cs` - round-trip wire-имён, регистронезависимый разбор,
  отклонение мусора, исключение на неизвестном коде enum.

Изменен только файл плана (строка статуса задачи 02). EF-схема задачи 01, `CommodityNameNormalizer`,
`CommodityCategoryHelper`, `IAiClient`, resolver, фон, промт, фронтенд, настройки, Go-сервисы,
MongoDB и nginx не тронуты.

## Как проверено

- `cd Analytics && dotnet build` - 0 ошибок, 30 предупреждений (все предсуществующие NU1902/NU1903)
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CategoryKeywordMatcher|FullyQualifiedName~CommodityCategoryKeywordRules|FullyQualifiedName~KeywordListType"` - 54/54 passed
- `cd Analytics && dotnet test` - 703/703 passed, 0 failed, 0 skipped
- `cd Analytics && dotnet test --filter "FullyQualifiedName~ProjectDependencyTests"` - 4/4 passed

## Итоги ревью

### Раунд 1 - CHANGES_REQUESTED

Blocker: длина слова считалась в UTF-16 code units (`word.Trim().Length`), тогда как FR-5
говорит о символах, а уже закоммиченный CHECK задачи 01 (`char_length(word) BETWEEN 2 AND 64`)
считает кодовые точки. Последствия были бы практическими: слово из одного астрального символа
(`"🙂"`) проходило валидацию и нарушало CHECK (23514) на вставке, а репозиторий задачи 03 по
ADR-035 п. 7 ловит только 23505, то есть POST отдавал бы 500 вместо 400; слово из 33-64
символов вне BMP отклонялось бы, хотя схема его допускает.

Исправлено: добавлен подсчёт кодовых точек (`EnumerateRunes`), `IsValidWord` сравнивает их
число с константами 2 и 64, в XML-doc зафиксирован паритет с CHECK схемы. Тесты: `"🙂"` -
невалидно, ровно 2 и ровно 64 символа-эмодзи - валидно, 65 - невалидно.

Внедрены все 5 suggestions ревьюера:

1. `ToWireName` для неизвестного кода теперь бросает `ArgumentOutOfRangeException` вместо
   молчаливого `boost`.
2. Снимок копирует переданную коллекцию (`Array.AsReadOnly(items.ToArray())`), doc объясняет
   изоляцию; добавлен тест.
3. Защитный фильтр пустого `normalized_word` покрыт тестом.
4. Добавлен тест, что название в верхнем регистре не совпадает (контракт «на вход приходит уже
   нормализованное название»).
5. Добавлены тесты `KeywordListTypeNames`.

### Раунд 2 - APPROVED

Blockers отсутствуют, регрессий матчера нет. Ревьюер самостоятельно прогнал тесты: полный
прогон 703/703, фильтр 54/54, `ProjectDependencyTests` 4/4.

Внесены два редакционных nit'а (только комментарии и doc, поведение не менялось):

- комментарий в тесте единиц измерения переформулирован как правило, без истории ревью;
- в doc констант `MinWordLength`/`MaxWordLength` указана единица измерения - символы
  (кодовые точки), как в `char_length`.

### Осознанные отличия от соседних аналогов

- `KeywordListTypeNames.ToWireName` бросает `ArgumentOutOfRangeException` на неизвестном коде,
  тогда как `CategorizationSourceNames.ToWireName` и
  `PrecategorizedCommodityStatusNames.ToWireName` возвращают fallback (`undefined`/`proposed`).
  Причина: у типа списка нет нейтрального значения - молчаливый `boost` записал бы в БД
  неверный список. Wire-имена и контракт API при этом не менялись.

### Заметка для задачи 03

При чтении слов из БД `KeywordListTypeNames.TryParse` вернет `false` для битого `list_type`
(значение вне CHECK, например след ручной правки), и если код просто проигнорирует результат,
переменная останется `Boost`. Поведение для такой строки (пропускать целиком, как невалидный
код категории по ADR-032/033, или логировать и продолжать) нужно осознанно решить в задаче 03;
в задаче 02 это не реализуется.

## Заметки для коммита

Рекомендуемое сообщение коммита:

```text
t-02 CommodityCategoryKeywords domain: list type, word rules, matcher, repo contract
```

На что обратить внимание при ручной проверке:

- в коммит должны попасть только 6 файлов Domain, 3 файла тестов и строка статуса в
  `docs/plans/commodity-categories.md`;
- **не включать** в коммит локально изменённые файлы, не относящиеся к задаче:
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
  `Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html`;
- матчер не должен содержать `DbContext`, `ILogger` или DI-зависимостей - это критерий
  готовности задачи;
- порядок подсказок проверяется тестами на перевёрнутом снимке, поэтому порядок строк в БД на
  выдачу не влияет.
