# Задача 03 — Golden corpus + Testcontainers-паритет SQL-backfill ↔ C# + контрольные запросы

- **Задача**: docs/plans/commodity-name-normalization.md#t-03-golden-corpus--testcontainers-паритет-sql-backfill--c--контрольные-запросы
- **Дата**: 2026-11-09
- **Ревью**: раундов — 1, вердикт APPROVED (блокеров нет)

## Что сделано

Закрыт риск дрейфа одноразовой SQL-копии правила очистки относительно C# (ADR-034 п. 2
«Идемпотентность и дрейф», открытый вопрос 6). Появился единый golden corpus
`CommodityNameNormalizationCorpus` (26 кейсов: вся таблица примеров ADR-034 п. 1, границы
5/6 цифр, кластеры хвостовых кодов, ведущий мусор `*#№`, fallback «имя только из кода»,
зафиксированные ограничения — дефис, запятая, код в середине, плюс кейс со схлопыванием
пробелов). Corpus — единственная точка правды: на нём строится theory тестов нормализатора
(рефакторинг `CommodityNameNormalizerTests` — ожидания не менялись, только источник данных)
и новый паритетный Testcontainers-тест `PrecategorizedNormalizedNameMigrationTests`, который
применяет **реальный файл миграции** `20261109120000_add_precategorized_normalized_name.sql`
целиком одним Npgsql-сеансом к «до-миграционной» схеме и сверяет backfill каждой
`applied`-строки с `CommodityNameNormalizer.NormalizeName` по всему corpus. Дополнительно
тест проверяет: `proposed` удалены (FR-5), `applied` сохранены в прежнем количестве,
`NOT NULL` (`INSERT` без ключа → `23502`), наличие обоих индексов, недоступность
`pg_temp`-функции после сеанса миграции. Контрольные запросы эксплуатации зафиксированы
в плане (раздел «Ответ на открытый вопрос №6 ADR») и перенесены в эту сводку чек-листом;
постоянный runbook — T-09. Продуктовый код и файл миграции не изменялись.

## Изменённые файлы

- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityNameNormalizationCorpus.cs` —
  новый golden corpus: `IReadOnlyList<(string Input, string Expected)> Cases` (26 кейсов) +
  `TheoryCases` — проекция Cases в `IEnumerable<object[]>` для xUnit `MemberData`
  (xUnit 2.5.3 не принимает `IReadOnlyList<ValueTuple>` — правило xUnit1019); ожидания
  живут только в `Cases`, дублирования нет.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityNameNormalizerTests.cs` —
  theory `NormalizeName_applies_canonical_cleaning_rule` переведена с 23 `InlineData` на
  `MemberData` по corpus; theory «код в середине» удалена (её 2 кейса теперь в corpus как
  `(input, input)`); из whitespace-theory удалён единственный перенесённый в corpus кейс
  `  МОЛОКО  3.2%  `. Отдельные факты (`null`, детерминизм, tr-TR, пунктуация,
  `Молоко 2.5%` vs `3.2%`, `""` и др.) — без изменения ожиданий; сумма кейсов 38 до и после.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj` —
  `<None Include>` файла миграции с `Link="Scripts/%(Filename)%(Extension)"` и
  `CopyToOutputDirectory="PreserveNewest"`: скрипт копируется в output тестовой сборки,
  путь в тесте строится от `AppContext.BaseDirectory` и не хардкодится.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedNormalizedNameMigrationTests.cs` —
  новый паритетный тест (один класс / один тест / один контейнер `postgres:16-alpine`):
  `EnsureCreatedAsync` → `ALTER TABLE ... DROP COLUMN normalized_name` (эмуляция
  «до-миграционной» схемы; DROP уносит индексы по колонке, скрипт пересоздаёт их точными
  именами из EF-конфига и SQL) → raw-SQL посев applied = весь corpus + 2 proposed (имена
  из corpus, другой магазин) → выполнение реального файла скрипта одним Npgsql-сеансом
  (`Pooling=false`) → утверждения (см. «Что сделано»). XML-doc объясняет, почему preflight
  `RAISE EXCEPTION` тестом не покрывается.
- `docs/plans/commodity-name-normalization.md` — только строка статуса T-03 в таблице
  «Ход выполнения» (`pending` → `in-progress` → `review` → `approved`).

Посторонние локальные правки — `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`appsettings.Development.json`, `wwwroot/index.html` — существовали до начала задачи, не
трогались и в коммит не входят.

## Как проверено

- `cd Analytics && dotnet build` (тестовый проект) — Build succeeded, 0 ошибок
  (NU190x warning'и pre-existing).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityNameNormalizer|FullyQualifiedName~PrecategorizedNormalizedNameMigration"`
  — Passed: 39, Failed: 0 (38 кейсов нормализатора + 1 паритетный тест).
- Критерий «правка regex → тест падает»: умышленно изменён хвостовой regex в SQL-скрипте
  (`[0-9]{6,}` → `[0-9]{7,}` в шаге 3) → паритетный тест упал
  (`Expected: "пирог" / Actual: "пирог 123456"`) → скрипт **откатан** из cp-бэкапа,
  `git diff` по файлу миграции пуст (проверено).
- `cd Analytics && dotnet test` — Passed: 609, Failed: 0 (~58–63 с). Базлайн после T-02 —
  608 / ~55 с: добавлен 1 тест, прирост времени в пределах бюджета (~6 с ≤ 15 с).
- Независимое ревью @code-reviewer (раунд 1): подтверждено применение **реального файла**
  миграции из output (текстовой копии скрипта в тесте нет); no-op backfill был бы пойман
  (`SET NOT NULL` упал бы на NULL, скрипт транзакционен); все 21 строка таблицы ADR п. 1
  присутствуют в corpus с дословными ожиданиями; рефакторинг ничего не потерял (38 кейсов
  до и после); `Link` со слэшем кроссплатформенно-безопасен (MSBuild нормализует);
  Testcontainers-гигиена чистая. Прогоны ревьюера: фильтр 39/39, полный набор 609/609 (~53 с).

## Итоги ревью

- Допущение исполнителя, одобренное ревьюером: посев applied = **весь corpus** (26 строк)
  вместо буквальной формулировки «2 applied» из сжатой постановки — план (шаг 5 раздела T-03
  и «Ответ на открытый вопрос №6») требует сравнения backfill «по всему corpus»; с двумя
  applied-строками умышленная правка хвостового regex или letter-check не была бы поймана.
  Имена в corpus уникальны, unique `(name, merchant_id)` посев проходит; proposed — от
  другого магазина.
- Оставлено без изменений (с обоснованием): preflight `RAISE EXCEPTION` отдельным тестом
  не покрывается — воспроизводимое расширение строки при `lower()` в PostgreSQL иное, чем
  `ToLowerInvariant()` в .NET (допущения ADR-034; в .NET 10 ICU `ToLowerInvariant` строку
  не расширяет — факт зафиксирован тестом T-02
  `TryAddProposalAsync_normalized_name_never_exceeds_raw_length_in_current_runtime`),
  поэтому аномальное удлинение в тесте воспроизвести нельзя; preflight остаётся страховкой
  на реальных данных и проверяется контрольными запросами на dev. Причина зафиксирована в
  XML-doc тестового класса.
- Исправлено по дешёвым suggestion'ам ревьюера (после вердикта APPROVED, тесты перезапускались):
  `Link="Scripts\..."` → `Link="Scripts/..."` в csproj (кроссплатформенно-безопасный слэш);
  косметическая правка прижатого комментария в `CommodityNameNormalizationCorpus.cs`.
  Перезапуски: фильтр 39/39, полный набор 609/609 (~63 с).

## Контрольные запросы после миграции (чек-лист эксплуатации)

Из плана, раздел «Ответ на открытый вопрос №6 ADR» (обязательный шаг эксплуатации после
применения миграции на dev/prod; постоянный runbook — T-09). Ручная проверка на dev-стенде
в этой задаче не выполнялась (стенд недоступен).

```sql
-- 1. Инвариант ключа: пустых/пропущенных ключей нет. Ожидается 0.
SELECT count(*) FROM precategorized_commodities
 WHERE normalized_name IS NULL OR btrim(normalized_name) = '';

-- 2. FR-5: неподтверждённых предложений не осталось (до старта фона). Ожидается 0.
SELECT count(*) FROM precategorized_commodities WHERE status = 0;

-- 3. applied сохранены: до/после миграции число строк status = 1 не уменьшилось.
SELECT status, count(*) FROM precategorized_commodities GROUP BY status ORDER BY status;

-- 4. Ключ не содержит ведущих/хвостовых кодов (для существующих строк). Ожидается 0.
SELECT count(*) FROM precategorized_commodities
 WHERE normalized_name ~ '^(?:[[:space:]]*[0-9]{6,})+'
    OR normalized_name ~ '(?:^|[[:space:]])[0-9]{6,}(?:[[:space:]]+[0-9]{6,})*[[:space:]]*$';

-- 5. Spot-check по примерам задачи (для строк, реально присутствующих в базе):
SELECT name, normalized_name
  FROM precategorized_commodities
 WHERE name IN ('2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ',
                '2195738 Туалетная бумага "СВАЁ" (3сл, 4р) 4810981001530',
                '*3455614 РЖ.КРАЙ Хлеб нар.300г');

-- 6. Схема: колонка NOT NULL и оба индекса на месте.
\d precategorized_commodities
```

Ожидаемые значения: п. 1, 2, 4 — `0`; п. 3 — `status = 0` отсутствует, `status = 1` не меньше
дорелизного; п. 5 — `пирог с вишней по ре`, `туалетная бумага "сваё" (3сл, 4р)`,
`рж.край хлеб нар.300г`; п. 6 — `normalized_name varchar(256) NOT NULL`,
`ix_precategorized_commodities_normalized_name`, `ux_precategorized_commodities_normalized_proposed`.

## Заметки для коммита

Рекомендуемое сообщение:

```text
test(analytics): golden corpus and SQL/C# parity test for name backfill

Add CommodityNameNormalizationCorpus as the single source of cleaning-rule
cases (full ADR-034 p.1 table, boundaries and documented limits), drive the
normalizer theory from it, and add a Testcontainers test that applies the real
20261109120000 migration script to a pre-migration schema and asserts the
backfilled normalized_name matches CommodityNameNormalizer across the whole
corpus (plus proposed deletion, applied retention, NOT NULL 23502, both
indexes and pg_temp function cleanup).

Task T-03 (docs/plans/commodity-name-normalization.md).
```

Чек-лист для `git add`:

- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityNameNormalizationCorpus.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedNormalizedNameMigrationTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityNameNormalizerTests.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj`
- `docs/plans/commodity-name-normalization.md` (только строка статуса T-03)
- `docs/reviews/commodity-name-normalization/03-golden-corpus-parity.md` (эта сводка)

**Не включать** в коммит: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` (посторонние локальные
правки). **Отдельно отметить:** в рабочем дереве есть незакоммиченная правка плана T-04
(ревизия декомпозиции после эскалации T-02 — решение о коммите принимает человек:
вместе с этим коммитом или отдельным док-коммитом).

Ручная проверка в этой задаче не выполнялась: dev-стенд недоступен; паритет backfill ↔ C#
покрыт постоянным автоматическим тестом, контрольные запросы выше — чек-лист для шага
эксплуатации (применение миграции, T-09).
