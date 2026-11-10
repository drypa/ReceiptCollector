# Задача 08 — Журнал решений: регресс (сырое имя + очищенный `normalized_name`)

- **Задача**: docs/plans/commodity-name-normalization.md#t-08-журнал-решений-регресс-сырое-имя--очищенный-normalized_name
- **Дата**: 2026-11-10
- **Ревью**: раундов — 1, вердикт APPROVED (блокеров нет)

## Что сделано

Задача тестовая: подтверждено, что `CategorizationDecisionRecorder` при записи событий хранит
**сырое** `commodity_name` (после `Trim` внешних пробелов) и **очищенный** `normalized_name`
(`CommodityNameNormalizer.NormalizeName`), с отдельными guard'ами на 256 символов для обоих полей
(ADR-034 п. 4.4 / F1, FR-4). Зафиксировано четыре сценария: имя с ведущим EAN-13 даёт сырое имя и
`пирог с вишней по ре` в ключе; имя из одного кода/пунктуации не даёт пустой ключ (fallback на
`base`, включая проверку формы fallback'а); имя ровно в 256 символов с ведущим кодом записывается,
а очищенный ключ укладывается в колонку; варианты одного товара с кодом и без дают два события с
**одинаковым** `normalized_name` и разными `commodity_name` — ключ сочетания ADR-033 остаётся
сырым. **Продуктовый код журнала не менялся**, расхождений тесты не вскрыли.

## Изменённые файлы

- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CategorizationDecisionRecorderTests.cs` —
  4 новых `[Fact]` (+72 строки, удалений нет) и уточнение xml-doc класса (регресс T-08, ADR-034
  п. 4.4 / FR-4);
  существующие кейсы валидации и «никогда не бросает» не тронуты по утверждениям:
  - `RecordAsync_name_with_leading_ean_keeps_raw_name_and_cleans_normalized_name` —
    `2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ` → `CommodityName` сырое,
    `NormalizedName == "пирог с вишней по ре"`;
  - `RecordAsync_code_only_name_gets_non_empty_fallback_within_column_guard` — `*3455614` →
    fallback `*3455614`; `*  #3455614` → fallback `* #3455614` (форма fallback'а отличается от
    входа за счёт collapse пробелов); оба ключа непустые и ≤ 256;
  - `RecordAsync_max_length_name_with_leading_code_is_written_with_shorter_normalized_name` —
    сырое имя ровно 256 символов с ведущим EAN записывается; `NormalizedName` = 242 символа;
  - `RecordAsync_variants_with_and_without_code_share_normalized_name_but_not_raw_name` —
    2 события, один `NormalizedName`, два разных `CommodityName`.
- `docs/plans/commodity-name-normalization.md` — таблица «Ход выполнения»: T-08
  (`pending → in-progress → review → approved`).
- `docs/reviews/commodity-name-normalization/08-journal-regression.md` — эта сводка.

Продуктовый код (`CategorizationDecisionRecorder`, `CategorizationDecision`,
`CategorizationDecisionEntity`/конфигурация, репозиторий журнала), миграции, DTO, эндпоинты,
настройки и frontend не тронуты.

## Как проверено

Исполнитель:

- `cd Analytics && dotnet test --filter "FullyQualifiedName~CategorizationDecisionRecorder"` —
  Passed: 29, Failed: 0, Total: 29 (73–78 мс).
- `cd Analytics && dotnet test` — Passed: 635, Failed: 0, Skipped: 0 (55 с, 1 м 3 с после
  suggestions); базлайн 631 + 4 новых Fact'а, после suggestions набор не вырос (второй вариант
  fallback'а добавлен внутрь существующего теста).
- Повторные прогоны после правок по suggestions ревьюера: фильтр 29/29, полный 635/635.
- `grep -rn "UPDATE categorization_decisions" --include=*.cs --include=*.sql --include=*.md .` —
  единственное совпадение — формулировка критерия готовности в самом плане
  (`docs/plans/commodity-name-normalization.md`); в коде, миграциях и скриптах таких операторов нет.
- `git status` до/после: посторонние правки `appsettings.json`, `appsettings.Development.json`,
  `wwwroot/index.html` существовали до старта и не трогались; git-операций не выполнялось.

Независимое ревью @code-reviewer (раунд 1, APPROVED):

- по коду recorder'а подтверждено: `Trim` сырого + guard 256 → `NormalizedName = NormalizeName(...)`
  + второй guard → оба поля попадают в событие как есть;
- ожидания всех четырёх тестов совпадают с фактическим поведением нормализатора (включая границу
  256/257 и fallback `*3455614`); проверено, какие кейсы падают при каждой мыслимой регрессии;
- кейс «два варианта» фиксирует ключ сочетания ADR-033 по сырому имени;
- продуктовый код не тронут (дифф содержит только тест и таблицу статусов), миграций/`UPDATE` нет,
  схема `varchar(256)` соответствует guard'ам, существующие утверждения не изменены;
- прогоны ревьюера: фильтр 29/29, полный 635/635.

## Итоги ревью

- Блокеров не было. Применены все три дешёвые suggestions ревьюера:
  1. комментарий про ADR-033 в тесте «два варианта» переформулирован как
     «ADR-033 (ключ сочетания / E1)»;
  2. fallback-кейс усилен: добавлен вход `*  #3455614` с проверкой, что fallback (`* #3455614`)
     отличается от входа буквально (collapse пробелов), а не просто «непустой»; тавтологичные
     `IsNullOrWhiteSpace`/`Length` оставлены как дополнительные guard-проверки;
  3. xml-doc класса дополнен строкой про регресс T-08 (ADR-034 п. 4.4 / FR-4).
  После правок прогоны повторены: фильтр 29/29, полный 635/635.
- **FR-4: накопленная история журнала не пересчитывается.** В диффе нет `UPDATE
  categorization_decisions`, нет миграций журнала (`Scripts/*.sql`), нет изменений EF-схемы
  (`CategorizationDecisionEntity`, `CategorizationDecisionConfiguration`) и репозитория; старые
  строки остаются как есть, очистка применяется только к `normalized_name` новых событий
  (ADR-034 F1; альтернативы F2 «нормализовать ключ сочетания» и F3 «пересчитать историю»
  отклонены ADR).
- Соответствие схеме: `commodity_name`/`normalized_name` — `varchar(256)`; recorder проверяет
  сырое имя (после `Trim`) и отдельно нормализованное (lowercase может удлинить строку), поэтому
  запись не может превысить колонку. Тест с 256 символами подтверждает прохождение границы,
  тест 257 — существующий кейс `RecordAsync_name_too_long_for_column_is_skipped`.
- Допущения:
  1. для имени из одного кода утверждается точное значение fallback (`*3455614` / `* #3455614`), а не
     только «непусто»: поведение детерминировано и покрыто тестами нормализатора (T-01);
  2. в кейсе максимальной длины ожидаемое `normalized_name` = 242 символа
     (256 − 14 символов `"2417268000006 "`), т. к. очистка только сокращает имя;
  3. известный флейк Testcontainers `mongo:7.0` (`Address already in use`/зависание) у ревьюера
     проявился один раз: мёртвые контейнеры удалены **без** `-v`, повторный прогон зелёный
     (635/635). У исполнителя оба полных прогона зелёные. Считать флейком среды, не регрессией T-08.
- Рамки T-08 соблюдены: пересчёт истории, ретеншен, ключ сочетания, изменения схемы и точки
  фиксации (чек / «Товары» / confirm) не затрагивались.

## Заметки для коммита

Рекомендуемое сообщение:

```text
T-08 journal regression: raw name + cleaned normalized_name
```

Чек-лист для `git add`:

- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CategorizationDecisionRecorderTests.cs`
- `docs/plans/commodity-name-normalization.md` — строки T-07 (`approved → committed`) и T-08
  (`pending → approved`) в таблице «Ход выполнения»
- `docs/reviews/commodity-name-normalization/08-journal-regression.md` (эта сводка)

**Не включать** в коммит: `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`
(личный AI-endpoint и Telegram-ID — категорически не в коммит),
`Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`,
`Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` — все три существовали до начала
задачи и не трогались. Утверждение «дифф по `src` пуст» из отчёта исполнителя неточно: в рабочем
дереве есть локальные правки этих трёх файлов, к T-08 они не относятся и в коммит не входят.

На что обратить внимание при ручной проверке:

- dev: после suggest + сохранения на чеке новое событие в журнале содержит сырое `commodity_name`
  (например, `2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ`) и очищенный `normalized_name`
  (`пирог с вишней по ре`); старые строки журнала не изменились;
- в журнале по-прежнему две строки для вариантов с кодом и без — это осознанно (FR-7 ADR-033,
  ключ сочетания по сырому имени);
- поиск/отображение журнала: `normalized_name` находится по очищенному ключу для обоих вариантов;
- полный `dotnet test` может один раз упасть/зависнуть на Testcontainers (`mongo:7.0`,
  `Address already in use`) — известный флейк среды, повторить прогон.
