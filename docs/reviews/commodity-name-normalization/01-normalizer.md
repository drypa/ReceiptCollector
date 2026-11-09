# Задача 01 — Нормализатор: правило очистки + юнит-тесты

- **Задача**: docs/plans/commodity-name-normalization.md#t-01-нормализатор-правило-очистки--юнит-тесты
- **Дата**: 2026-11-09
- **Ревью**: раундов — 1, вердикт APPROVED (блокеров нет)

## Что сделано

Каноническое правило очистки названий (ADR-034 п. 1) реализовано в единственной функции
`CommodityNameNormalizer.NormalizeName`, поэтому все существующие call sites (кэш, resolver,
пакетный suggest, фон предкатегоризации, журнал решений) получают новый ключ автоматически, без
правок в этих местах. Прежнее поведение (`null → ArgumentNullException`, lowercase, trim,
схлопывание пробелов) сохранено. Добавлены три `[GeneratedRegex]`: ведущий мусор + коды, хвостовой
кластер кодов, проверка наличия буквы; при пустом результате или отсутствии букв возвращается
нормализованный оригинал (`base`) — пустой ключ не появляется. XML-doc описывает шаги правила,
fallback и ссылается на ADR-034 п. 1, NFR-3.1 ADR 009 и NFR-3.

Юнит-тесты покрывают всю таблицу примеров ADR-034 п. 1 (21 кейс теории, включая документированные
ограничения с дефисом и запятой), плюс отдельные факты: код в середине названия не снимается,
детерминизм, независимость от текущей культуры (`tr-TR`), `null → ArgumentNullException`.
Существующие кейсы (`""`, `Молоко 2.5%` vs `3.2%`, пунктуация, регистр/пробелы) не изменены.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityNameNormalizer.cs` —
  пять шагов правила ADR-034 п. 1, три новых `[GeneratedRegex]`, хелпер `CollapseWhitespace`,
  обновлённый XML-doc; добавлен завершающий перевод строки в конце файла.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityNameNormalizerTests.cs` —
  теория по таблице примеров ADR (21 кейс + 2 граничных кейса по итогам ревью), факты
  «код в середине», детерминизм, культурная независимость, `null`; прежние тесты без изменения
  ожиданий.
- `docs/plans/commodity-name-normalization.md` — только строка статуса T-01 в таблице
  «Ход выполнения» (`pending` → `in-progress` → `review` → `approved`).

Посторонние локальные правки — `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`appsettings.Development.json`, `wwwroot/index.html` — существовали до начала задачи, не трогались
и в коммит не входят.

## Как проверено

- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityNameNormalizer"` — Passed: 38,
  Failed: 0, Skipped: 0 (23 мс).
- `cd Analytics && dotnet test` — Passed: 604, Failed: 0, Skipped: 0 (~49 с), включая
  Testcontainers-наборы и `Architecture/ProjectDependencyTests`; конфликтов контейнеров не было.
- Независимое ревью @code-reviewer (раунд 1): regex сверены посимвольно с ADR-034 п. 1, на 19 входах
  проверено отсутствие лишнего снятия и корректное снятие кодов, fallback «пусто ИЛИ нет букв»
  подтверждён; прогоны ревьюера — фильтр 35/35, полный набор 601/601 (на момент ревью, до
  добавления трёх неблокирующих кейсов).

## Итоги ревью

- Исправлено по замечаниям ревьюера (неблокирующие suggestions, применены после APPROVED):
  - добавлен тест культурной независимости: под `CultureInfo.CurrentCulture = new CultureInfo("tr-TR")`
    результат не меняется (ожидание вычисляется до смены культуры, культура восстанавливается в
    `finally`); контрольная проверка `2417268000006 IPHONE ХЛЕБ → iphone хлеб` ловит замену
    `ToLowerInvariant` на `ToLower`;
  - добавлены два граничных кейса: `хлеб * → хлеб *` (хвостовой мусор не снимается) и
    `хлеб 12345 789012 → хлеб 12345` (5-значный сосед не мешает снятию хвостового кода).
- Оставлено без изменений (с обоснованием):
  - `RegexOptions.NonBacktracking` не включаем: ADR-034 п. 1 его «допускает», а не требует; шаблоны
    без lookaround/backreference, классы символов в альтернативах `^(?:[\s*#№]+|[0-9]{6,})+`
    непересекающиеся, вход ограничен длиной названия товара (≤ 256), поэтому риск катастрофического
    backtracking пренебрежим; выбор `RegexOptions.None` консистентен с существующим
    `WhitespaceRegex`.
  - Класс букв `[A-Za-zА-Яа-яЁё]` оставлен как в ADR (зафиксированная семантика): имена целиком из
    `ї/є/ґ` или греческих/диакритических букв уходят в fallback и сохраняют код. В T-01 не меняется;
    вынесено как пункт для открытых вопросов ADR-034 — архитектор допишет отдельно.

## Заметки для коммита

Рекомендуемое сообщение:

```text
feat(analytics): canonical commodity name cleaning in normalizer

Implement ADR-034 p.1 cleaning rule (leading noise/codes, trailing code
cluster, base fallback) in the single CommodityNameNormalizer.NormalizeName;
cover the full ADR example table and edge cases with unit tests.

Task T-01 (docs/plans/commodity-name-normalization.md).
```

Чек-лист для `git add`:

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityNameNormalizer.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityNameNormalizerTests.cs`
- `docs/plans/commodity-name-normalization.md` (только строка статуса T-01)
- `docs/reviews/commodity-name-normalization/01-normalizer.md` (эта сводка)

**Не включать** в коммит: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` (посторонние локальные правки).

Ручная проверка не требуется: изменение — чистая детерминированная функция, покрытая юнит-тестами;
контрольные запросы в DevTools/psql для этой задачи не нужны (схема БД не меняется). Достаточно
проверить диффом, что правки ограничены нормализатором, его тестами и строкой статуса в плане.
