# ADR-033: Журнал решений категоризации товаров (предложено → принято)

## Статус

Принято (проектирование). Основание — задача
[categorization-decision-log](../tasks/categorization-decision-log.md)
(статус Draft, автор user, дата 2026-11-06; FR-1…FR-8, NFR-1…NFR-3, критерии приёмки
и edge cases обязательны к исполнению). Реализация — по плану (декомпозиция задачи
в `docs/plans/`).

**Дополняет, ничего не отменяет:** [ADR 009](009-auto-commodity-categorization.md)
(контракты suggest/apply чека), [ADR 019](019-in-memory-commodity-category-cache.md)
(in-memory кэш), [ADR 027](027-background-commodity-precategorization.md)
(очередь `precategorized_commodities`, стиль админских эндпоинтов, `MigrationRunner`),
[ADR 028](028-commodities-page-categorization.md) (пакетное сохранение страницы «Товары»,
общий resolver, helper `AdminAuthorization`), [ADR 032](032-merchant-default-commodity-category.md)
(источник `merchant-default`, безопасное чтение кодов категорий).

> **Вне области (зафиксировано задачей):** любое автопроставление категорий (заказчик —
> категорическое «нет»); персистентная память «имя → подтверждённая категория»; проценты
> точности, графики, матрицы путаницы, счётчики и группировки в UI; экспорт; восстановление
> истории задним числом (журнал начинается с момента внедрения); изменения Go-сервисов
> (`backend/`, `bot/`), MongoDB и nginx.

## Контекст

### Проблема

Система не сохраняет историю решений категоризации: «ИИ предложил X — человек выбрал Y»
нигде не фиксируется, и каждое подтверждённое решение безвозвратно теряется в момент
сохранения. Невозможно понять, где ИИ ошибается, какие категории путаются, какие названия
исправляются снова и снова. Требуется **наблюдать, не меняя**: при каждом подтверждении
человеком категории, для которой ранее выдавалось предложение, записывать сырое событие;
администратору — страница просмотра с фильтром «только расхождения». Текущий пользовательский
поток и все существующие контракты должны остаться неизменными (NFR-1, NFR-2), а ошибки
собственного журнала не имеют права делать пользовательское сохранение неуспешным (NFR-3).

### Текущее состояние кода (факты, проверены по коду 2026-11-06)

| Место | Факт | Значение для решения |
|---|---|---|
| `Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs` | `PUT /api/receipts/{receiptId:guid}/categories`: `SaveCategoriesRequest(Items)`, элемент `SaveCategoryItemRequest(CommodityId, Category)`; пустая строка = сброс в `null`; доступ — любому аутентифицированному владельцу чека (admin-проверки нет) | Точка фиксации № 1. Аддитивные **опциональные** поля снимка предложения не ломают контракт; флаг «админ» для этого пути нужно отдельно читать из `users` |
| `Infrastructure/Modules/Commodities/CommodityCategorizationService.ApplyConfirmedCategoriesAsync` | Грузит чек (owner-scoped), по каждой assignment — `UpdateCategoryAsync` (отдельный `SaveChangesAsync`), имя позиции известно (`receipt.Items` → `commodity.Name`), чужие `commodityId` молча игнорируются; `_cache.TryAdd` — после записи | Имена и итог для события есть под рукой; события — одним батчем после цикла применения; «игнорированная» assignment события не даёт |
| `Infrastructure/Modules/Commodities/CommodityBatchCategorizationService.ApplyAsync` | Сохраняются только реально изменённые строки (`changed`: old ≠ new); `current` (с `Name`) грузится до записи одной транзакцией; эндпоинт `PUT /api/commodities/categories` — **admin-only** | Точка фиксации № 2: события по строкам из `changed`, флаг «админ» — `true` без доп. запроса |
| `Api/Modules/Commodities/CommodityBatchCategorizationEndpoints.cs` | `SaveCommodityCategoryItemRequest(Guid CommodityId, string? Category)`; `null`/пусто = «не трогаем» (assign-only, C4) | Аддитивные поля снимка в тот же элемент; assign-only семантика не меняется |
| `Infrastructure/Modules/Commodities/PrecategorizedCommodityService.ConfirmAsync(id)` | Сначала читает строку очереди, затем `ConfirmAsync(id, proposal.ProposedCategory)` — одна транзакция (ADR-027 п. 4), `TryAdd` в кэш после commit; `PUT .../{id}/category` **перезаписывает** `proposed_category_id` | Точка фиксации № 3. Исходное предложение после правки админа в таблице очереди **теряется** — нужна отдельная колонка (решение D). В `ConfirmAsync` нужно передать принявшего |
| `Application/.../Models/CategorizationModels.cs` | `CategorizationSource { Existing, MerchantDefault, Cache, Precategorized, Ai, Undefined }`; wire-имена lowercase (`merchant-default` — дефис) | Источники журнала — ровно 4: `ai` / `precategorized` / `cache` / `merchant-default` (FR-1); `existing`/`undefined` — не предложения |
| `Api/Modules/Users/UserContext.cs`, `AdminAuthorization.cs` | `UserContext.UserId` (AsyncLocal, ставит `UserAuthCookieMiddleware`); `AdminAuthorization.AuthorizeAsync` — `401` / явный `Results.StatusCode(403)` (ADR-027/028) | Принявший = `UserContext.UserId`; admin-флаг — снимок `users.is_admin` на момент решения; права чтения журнала — тем же helper'ом |
| `Persistence/Postgres/UserEntity.cs`, `users` | `id`, `name varchar(256)`, `external_id`, `telegram_id`, `is_admin` | Имя принятого — JOIN на чтении (edge case «имя недоступно → пусто»); FK в журнал не кладём (решение F) |
| `Domain/Modules/Commodities/CommodityNameNormalizer.cs` | `NormalizeName`: trim + lower + схлопывание пробелов; один источник правила (кэш ADR-019, очередь ADR-027) | Журнал хранит `normalized_name` той же функции — второй нормы не появляется |
| `frontend/src/components/ReceiptDetails.tsx` | После suggest в состоянии `suggestions` — по позиции `{ categoryId, source, error }`; `selectedCategories` предзаполнен; сохранение шлёт **все** позиции | Клиент в момент сохранения знает «что было предложено» по каждой позиции — клиентский снимок реализуем без хаков |
| `frontend/src/hooks/useCommodityPageCategorization.ts` | `DraftEntry { value, initialValue, source?, touched }`; в `PUT` уходят только `changed`; при ручной правке `value` меняется, **предложенная категория в черновике не сохраняется отдельно** | Фронту нужен аддитивный снимок `suggested { value, source }`, не изменяемый ручными правками |
| `Persistence/Postgres/*Repository.cs` | Проект использует EF Core LINQ + `ExecuteUpdateAsync`; **raw SQL не используется нигде**; поиск — `EF.Functions.ILike`; пагинация — `limit/offset` + `X-Total-Count` | Дедуп на чтении — LINQ anti-join (решение E), тестируемый на EF InMemory; сырой `DISTINCT ON` — запасной путь |
| `Migrations/MigrationRunner` | Транзакция на скрипт, учёт в `migration_scripts_history` (ADR-027 п. 3) | Журнал и колонка очереди — обычные SQL-скрипты в `Scripts/` |
| `Domain/Modules/Commodities/CommodityCategoryHelper.cs` | `GetDisplayName` — по enum, неизвестное значение → «Не указана»; справочника-таблицы нет (ADR 005) | Денормализованное имя категории в журнал не пишется (прецедент D9 ADR-027); отображаемое имя считается на чтении |

### Требования (из задачи, сводка)

- **FR-1** — событие: исходное название, нормализованное название, источник предложения
  (`ai`/`precategorized`/`cache`/`merchant_default`), предложенная категория, итоговая
  категория, принявший (id + флаг «админ» + имя, если известно).
- **FR-2/FR-3** — все четыре источника; фиксируются и согласия, и исправления.
- **FR-4** — точки фиксации: страница чека, страница «Товары», подтверждение в `/precategorized`.
- **FR-5** — не фиксируется: показали-но-не-сохранили; категория проставлена без предложения.
- **FR-6** — страница и API только для администратора; фильтры «только расхождения», источник,
  предложенная/итоговая категория, поиск по названию, период; пагинация.
- **FR-7** — в выдаче только уникальные сочетания «название + источник + предложено + принято»,
  «кто принял» — от последнего события; счётчики/агрегаты не нужны.
- **FR-8** — хранение сырое, по событию на каждое подтверждение; дедуп только на чтении.
- **NFR-1** — автопроставление запрещено; поведение suggest/сохранения/подтверждения не меняется.
- **NFR-2** — существующие контракты не ломаются; интерфейс обычных пользователей не меняется.
- **NFR-3** — запись журнала не заметно замедляет сохранение и не может сделать его неуспешным.

## Рассмотренные варианты

### Ключевое решение A: откуда известно «что было предложено» в момент сохранения

Ключевая трудность: suggest **stateless** (ADR-009/028) и ничего не хранит; между показом
предложения и сохранением состояние конвейера может измениться (кэш пополняется чужим
сохранением, админ подтвердит строку очереди, сменится дефолт магазина, ИИ ответит иначе).
Единственный компонент, который знает, **что именно видел человек**, — клиент.

#### Вариант A1: Снимок предложения от клиента в сохранении (аддитивные поля) — **выбран**

Клиент, у которого в состоянии лежат предложения (`suggestions` на странице чека,
`draft` на странице «Товары»), добавляет в сохраняемый элемент пару опциональных полей:
`suggestedCategory` (имя enum) + `suggestionSource` (wire-имя). Сервер пишет событие по
сохранённой строке, если валидный снимок присутствует.

**Плюсы:**
- Журналируется ровно то, что видел человек, — «источник истины» отображения; гонки между
  suggest и save перестают иметь значение (событие фиксирует пару «предложено на экране →
  принято при сохранении»).
- Сервер остаётся stateless: нет сессий, TTL, памяти между запросами (консистентно с
  отклонением серверной сессии в ADR-028, вариант E).
- Контракт строго аддитивен: без полей — старое поведение и никаких событий (NFR-2, FR-5b
  «проставил без предложения» выполняется построением: у UI нет снимка, когда предложений
  не было).
- Фронтенд уже хранит нужные данные (см. фактическую таблицу) — минимальный diff.

**Минусы:**
- Снимок приходит от клиента → его можно исказить. Принято: журнал наблюдательный, вред
  ограничен «мусором в собственном журнале» от своего же пользователя; сервер валидирует
  форму (источник ∈ 4, категория — валидное имя enum ≠ `Undefined`) и игнорирует невалидный
  снимок без ошибки сохранения. Жёсткий серверный снимок — путь эволюции (открытый вопрос 5).

#### Вариант A2: Повтор резолвера (или лёгкого поиска по источникам) в момент сохранения — **отклонено**

**Минусы:** состояние конвейера между suggest и save меняется по построению (собственное
же сохранение кладёт категорию в кэш — ADR-019; подтверждения `/precategorized` и кэш —
сквозные). Повтор покажет **другое** предложение или предложение там, где человек его не
видел: «сохранение категории без предложения» (FR-5b) стало бы событий — прямое нарушение
требования. Плюс лишие чтения кэша/очереди/мерчантов на каждом сохранении, а повторный
ИИ-вызов абсурден по цене. Журнал должен фиксировать факт отображения, а не «что конвейер
ответил бы сейчас».

#### Вариант A3: Серверное хранилище снимков предложений (память/TTL по userId) — **отклонено**

**Минусы:** серверное состояние, TTL, вытеснение, привязка к экземпляру, поведение при
рестарте и мультиэкземплярности — ровно то, от чего отказались в ADR-028 (вариант E).
Сложность ради гарантии, для которой достаточно клиентского снимка (наблюдательные данные,
не финансовая операция).

#### Вариант A4: Отдельный эндпоинт «журнал», в который пишет клиент после успеха сохранения — **отклонено**

**Минусы:** неатомарность (сохранение успело, запись журнала — отдельный запрос, который
может не состояться); итоговая категория и принявший определялись бы клиентом, а не
сервером (источник истины уезжает); появляется новый «сырой» API-поверхности для записи
любому пользователю. Итог, кто принял и когда, обязан фиксировать сервер в своём
сохраняющем пути.

### Ключевое решение B: модель хранения

#### Вариант B1: Новая таблица сырых событий `categorization_decisions` в PostgreSQL — **выбран**

По событию на каждое подтверждение (FR-8), дедуп — только на чтении. Таблица — в той же БД
`receipts` (Analytics), миграция — обычный SQL-скрипт `MigrationRunner` (прецеденты:
`precategorized_commodities` ADR-027, `category_source` ADR-032). Источники категорий в
коде — wire-имена; коды категорий — integer без FK (справочник — enum, ADR 005; прецедент
`proposed_category_id`).

**Плюсы:** durable; согласовано с паттернами проекта (EF, репозитории, snake_case); индексы
под фильтры/дедуп; не зависит от жизненного цикла чеков (см. F).

#### Вариант B2: Дедуп на записи (таблица уникальных сочетаний, upsert «последний принявший») — **отклонено**

**Минусы:** нарушает FR-8 («сырое хранение, по событию на каждое подтверждение»): теряется
история (когда и сколько раз решение повторялось), «последняя запись побеждает» искажает
журнал как исторические данные. Задача прямо требует дедуп уровня отображения.

#### Вариант B3: In-memory / Redis — **отклонено**

**Минусы:** журнал обязан переживать рестарт и деплой (исторические данные); Redis — новая
инфраструктура ради одного инстанса (прецедент отказа ADR-027 C, ADR-019 A).

#### Вариант B4: Транзакционный outbox + фоновая дозапись — **отклонено (зафиксирован путь эволюции)**

**Минусы:** outbox-инфраструктуры в проекте нет; ради одной дешёвой INSERT-пачки в том же
запросе — overkill (KISS/YAGNI). Если сохранение станет чувствительным к +1 запросу —
естественная эволюция (та же таблица, отправка фоном).

### Ключевое решение C: транзакционность и поведение при ошибках записи (NFR-3)

#### Вариант C1: После commit пользовательского сохранения, один батч-INSERT, recorder никогда не бросает — **выбран**

Журнал пишется **после** успешной фиксации категорий (порядок как у `cache.TryAdd` в
ADR-019/027/032): все события запроса — одним `AddRangeAsync` (один `SaveChangesAsync`),
любой сбой recorder'а (валидация, БД, отмена) проглатывается с `LogError` и не влияет на
ответ. Невалидный снимок отдельного элемента — пропуск события с `LogWarning`, остальные
пишутся.

**Плюсы:** NFR-3 выполняется структурно — у журнала физически нет канала повлиять на
пользовательский результат (он пишется после commit и никогда не бросает); издержки —
минимальны (+1 round-trip с батчем INSERT на запрос сохранения, на порядки меньше
ИИ-вызовов того же сценария).

**Минусы:** crash между commit сохранения и записью журнала теряет события. Осознанно:
наблюдательный best-effort журнал; альтернатива (C2) дороже по рискам.

#### Вариант C2: Запись журнала в той же транзакции, что и сохранение категорий — **отклонено**

**Минусы:** сбой INSERT журнала откатывал бы пользовательское сохранение — прямое нарушение
NFR-3 («ошибочная запись журнала не должна ронять пользовательское действие»). Плюс
удлинение транзакций (страница «Товары» — до 200 id) и смешение ответственностей.

#### Вариант C3: Fire-and-forget запись из запроса (отдельный Task/канал) — **отклонено**

**Минусы:** `UserContext`/scoped-DI за пределами запроса, неконтролируемые задачи при
shutdown, невыполнимая детерминированность в тестах; выигрыш (миллисекунды) не оправдан.

### Ключевое решение D: исходное предложение в `/precategorized`

Сценарий задачи: админ **исправляет** предложение (`PUT .../category`) и подтверждает —
исправление должно попасть в журнал как «предложено ≠ принято». Но `PUT` перезаписывает
`proposed_category_id`, и к моменту confirm исход предложения в очереди утрачен.

#### Вариант D1: Колонка `original_proposed_category_id` + backfill — **выбран**

`precategorized_commodities` получает колонку «первоначальное предложение», которую пишет
только фон (при создании строки); `PUT .../category` её не трогает; confirm пишет событие
`предложено = original_proposed_category_id`, `принято = proposed_category_id` (текущий).
Миграция: ADD COLUMN → `UPDATE` backfill = текущий `proposed_category_id` → SET NOT NULL
(исторические строки: считаем исходным то, что есть; журнал и так начинается с момента
внедрения).

**Плюсы:** история правок глубиной в один шаг (этого достаточно для FR-3); confirm не
нуждается в клиентских данных; минимальная аддитивная миграция малой таблицы.

#### Вариант D2: В событие писать `предложено = принято = текущий proposed_category_id` — **отклонено**

**Минусы:** все события этого пути — «согласия», фильтр «только расхождения» для источника
`precategorized` всегда пуст: главная аналитическая ценность для этого пути теряется.

#### Вариант D3: Полная таблица истории правок очереди — **отклонено**

**Минусы:** ещё одна сущность и путь записи ради данных, которые нужны только в момент confirm
(YAGNI).

### Ключевое решение E: дедупликация на чтении (FR-7) + пагинация

#### Вариант E1: LINQ anti-join (greatest-n-per-group) в EF Core — **выбран**

Фильтр по событиям → выборка событий, для которых **нет более свежего события того же
сочетания** (`WHERE NOT EXISTS (…same combination… AND (created_at, id) > (d.created_at,
d.id))`) → LEFT JOIN `users` за именем принявшего → порядок `created_at DESC, id DESC` →
`Skip/Take`; `Total` = `Count()` того же anti-join (число уникальных сочетаний →
`X-Total-Count`). Ключ сочетания: `(commodity_name, suggestion_source,
suggested_category_id, final_category_id)` — исходное название входит в ключ (edge case
«Хлеб»/«ХЛЕБ» — разные строки; `normalized_name` в ключе не нужен, он функция имени).

**Плюсы:** выражимо стандартным EF LINQ (проект raw SQL не использует — см. фактическую
таблицу), корректно и тестируется на EF InMemory (как существующие тесты); фильтрация и
пагинация серверные.

**Минусы:** анти-соединение по растущей таблице — нужен составной индекс под сочетание +
`created_at DESC, id DESC` (клауза «E» индексов ниже); план замерить по эксплуатации
(прецедент «отложенное решение» ADR-027).

#### Вариант E2: Raw SQL `DISTINCT ON (сочетание) … ORDER BY …, created_at DESC` — **отклонено**

Postgres-специфично, появился бы первый raw SQL в проекте, недоступно тестам на EF InMemory.
Остаётся записанным запасным путём, если EF-трансляция anti-join окажется медленной.

#### Вариант E3: Материализованное представление сочетаний — **отклонено**

**Минусы:** инфраструктура refresh (триггеры/фон) ради админской страницы низкочастотного
доступа (KISS/YAGNI).

#### Вариант E4: Дедуп на клиенте поверх «сырого» списка — **отклонено**

**Минусы:** ломает пагинацию и `X-Total-Count` (число строк страницы не равно числу
сочетаний), перекачивает данные.

### Ключевое решение F: связь события с чеком/позицией и пользователем

#### Вариант F1: Принявший — `accepted_by uuid` (без FK) + снимок `accepted_by_admin boolean`; имя — JOIN на чтении; ссылки на чек/позицию не хранятся — **выбран**

- Принявший берётся из текущей авторизации Analytics (`UserContext.UserId`,
  `UserAuthCookieMiddleware`) — сервер, а не клиент (см. A4). Флаг «админ» — **снимок**
  `users.is_admin` на момент решения (роль человека может измениться; журнал фиксирует,
  кем он был, когда принимал). Имя (`users.name`) не денормализуется: JOIN на чтении;
  пользователь удалён/имя недоступно → id остаётся, имя в UI пустое (edge case задачи).
- **FK на `users` не кладётся**: журнал — автономные исторические данные; `CASCADE` терял бы
  события при удалении пользователя, `RESTRICT` блокировал бы удаление. Прецедент FK —
  бизнес-объекты (`receipts`), не журнал.
- **Ссылки `commodity_id`/`receipt_id` не хранятся**: единица наблюдения — решение по
  **названию** («предложено → принято»), а не по строке таблицы: confirm `/precategorized`
  касается тысяч позиций сразу (ссылку не выбрать), позиции удаляются каскадом с чеками.
  Позиция как факт остаётся в `commodities`. Отложенная возможность — см. открытый вопрос 6.

#### Вариант F2: Денормализация имени пользователя в событие — **отклонено**

Имя пользователя может переименовываться (прецедент ADR 011 — редактирование имён);
снимок имени показал бы устаревшее, JOIN — актуальное; FR-1 требует «имя, если известно».

## Решение

Сводка принятых решений: **A1** (клиентский снимок предложения — аддитивные опциональные
поля в обоих save-контрактах), **B1** (сырая таблица `categorization_decisions` в
PostgreSQL), **C1** (запись после commit, батчем, recorder никогда не бросает), **D1**
(колонка `original_proposed_category_id` в очереди предкатегорий), **E1** (дедуп на чтении
LINQ anti-join), **F1** (принявший: id + снимок admin-флага, имя JOIN'ом; FK и ссылок на
позиции нет).

### Обоснование

1. **Единственный корректный источник «предложено»** — снимок с клиента: только он знает,
   что видел человек, при stateless suggest и изменяющемся состоянии конвейера (A2 даёт
   ложные события при сохранении без предложения — нарушение FR-5b).
2. **Журнал наблюдает, не участвуя** (NFR-1): новые пути никогда не пишут в `commodities`;
   автоприменения (импорт чека, дожим дефолтом — ADR-032) событий не дают: там нет
   «предложения, подтверждённого человеком».
3. **NFR-3 структурно**: запись — после commit, одним батчем, `catch`-всё; ни один код
   ответа и ни одно число (`updated`, тосты) от журнала не зависят; невалидный снимок —
   не ошибка сохранения, а пропущенное событие с Warning.
4. **Соответствие паттернам проекта**: миграции `MigrationRunner`, EF + snake_case,
   wire-имена источников, текстовые ошибки API без ProblemDetails, `AdminAuthorization`,
   `limit/offset` + `X-Total-Count`, `EF.Functions.ILike`, TimeProvider для времени
   (тестируемость, ADR-027 NFR-9).
5. **KISS/YAGNI**: нет фоновых сервисов, outbox, matview, Redis, новых env-настроек
   (журнал — данные; emergency-выключатель не нужен: цена записи — батч INSERT, отказо-
   устойчивость — «никогда не бросает»).

### 1. Схема PostgreSQL и миграция

Скрипт `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261106120000_add_categorization_decisions.sql`
(транзакция на скрипт, учёт в `migration_scripts_history` — повтор идемпотентен):

```sql
CREATE TABLE categorization_decisions
(
    id                    uuid PRIMARY KEY,
    commodity_name        varchar(256) NOT NULL,   -- исходное название, как в чеке
    normalized_name       varchar(256) NOT NULL,   -- CommodityNameNormalizer.NormalizeName
    suggestion_source     varchar(32)  NOT NULL,   -- wire-имена: ai | precategorized | cache | merchant-default
    suggested_category_id integer      NOT NULL,   -- код CommodityCategory, != 0
    final_category_id     integer      NOT NULL,   -- итог; 0 = Undefined («Не указана»)
    accepted_by           uuid         NOT NULL,   -- users.id, FK осознанно нет (F1)
    accepted_by_admin     boolean      NOT NULL,   -- снимок users.is_admin на момент решения
    created_at            timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ck_categorization_decisions_suggested_not_undefined
        CHECK (suggested_category_id <> 0),
    CONSTRAINT ck_categorization_decisions_final_non_negative
        CHECK (final_category_id >= 0)
);

-- Дедуп-выборка (E1) и группировки сочетаний: greatest-n-per-group по (сочетание → свежее).
CREATE INDEX ix_categorization_decisions_combination
    ON categorization_decisions
       (commodity_name, suggestion_source, suggested_category_id, final_category_id, created_at DESC, id DESC);

-- Порядок списка по умолчанию, фильтр периода, будущая ретенция.
CREATE INDEX ix_categorization_decisions_created_at
    ON categorization_decisions (created_at DESC);

-- Основной фильтр админской страницы: «только расхождения».
CREATE INDEX ix_categorization_decisions_mismatch
    ON categorization_decisions (created_at DESC)
    WHERE suggested_category_id <> final_category_id;

-- D1: неизменяемый исходный пропозал строки очереди (PUT .../category его не трогает).
ALTER TABLE precategorized_commodities
    ADD COLUMN original_proposed_category_id integer;

UPDATE precategorized_commodities
   SET original_proposed_category_id = proposed_category_id
 WHERE original_proposed_category_id IS NULL;

ALTER TABLE precategorized_commodities
    ALTER COLUMN original_proposed_category_id SET NOT NULL;
```

- `suggestion_source` — **без CHECK по перечню**: допустимое множество — свойство кода
  (enum `CategorizationSource`), проверка — на записи (recorder); расширение источников не
  требует миграции. Аналогично коды категорий — без FK (справочник — enum, ADR 005;
  прецеденты `proposed_category_id` ADR-027, `category_source` ADR-032).
- Отображаемые имена категорий не хранятся (прецедент D9 ADR-027): `Enum.IsDefined` +
  `CommodityCategoryHelper.GetDisplayName` на чтении; удалённый из справочника код
  отображается как «—» с числом (безопасное чтение ADR-032).
- Итоговая `NULL`-категория сохранения (сброс на странице чека) нормализуется в `0`
  (`Undefined`) — один канонический «нет категории» (D11-семантика ADR-027).
- **Рост таблицы.** События порождаются только ручными подтверждениями с предложением:
  разовый разбор бэклока `/precategorized` (~4000+ строк) ≈ столько же событий; штатный
  день — десятки–сотни. Даже 10⁶ строк — комфортный объём PostgreSQL при индексах выше;
  агрегаты в UI отсутствуют, страница одна и низкочастотная. Политика ретеншена
  (чистка/архив по возрасту) — **открытый вопрос 1** (по факту: журнал — актив,
  автоудаления нет).

### 2. Домен, контракты, слои (карта для плана)

| Слой | Новое / изменённое |
|---|---|
| Domain | `Modules/CategorizationDecisions/CategorizationDecision.cs` (record: `Id`, `CommodityName`, `NormalizedName`, `SuggestionSource`, `SuggestedCategory`, `FinalCategory`, `AcceptedBy`, `AcceptedByAdmin`, `CreatedAt`); `ICategorizationDecisionRepository` (`AddRangeAsync`, `GetCombinationsAsync(query)`, `CountCombinationsAsync(query)`); изменён `PrecategorizedCommodity` — поле `OriginalProposedCategory` |
| Application | `Modules/CategorizationDecisions/Contracts/ICategorizationDecisionRecorder.cs` — `RecordAsync(IReadOnlyCollection<CategorizationDecisionDraft>, ct)`, **контрактно никогда не бросает** (C1); `CategorizationDecisionDraft(CommodityName, SuggestionSource, SuggestedCategory, FinalCategory, AcceptedByUserId, AcceptedByAdmin)`; `Contracts/ICategorizationDecisionReadService.cs` — `GetPageAsync(CategorizationDecisionQuery)` → `(Items, Total)`; `Models/CategorizationDecisionModels.cs` — `CategorizationDecisionQuery(OnlyMismatch, Source, SuggestedCategoryId, FinalCategoryId, Search, From, To, Limit, Offset)`, `CategorizationDecisionRow` (int-коды + имена + `AcceptedByName?` + `DecidedAt`); допустимое множество источников — `CategorizationDecisionSources.All` (4 wire-имени). Изменения аддитивные: `ConfirmedCategoryAssignment` += `CommodityCategory? SuggestedCategory`, `string? SuggestionSource`; `CommodityCategoryChange` += те же; `IPrecategorizedCommodityService.ConfirmAsync(Guid id, Guid acceptedByUserId, ct)` |
| Infrastructure | `Modules/CategorizationDecisions/CategorizationDecisionRecorder.cs` (валидация/фильтрация draft'ов, `NormalizeName`, `TimeProvider.GetUtcNow`, один `AddRangeAsync`, `catch (Exception)` → `LogError`; `LogWarning` на пропущенные); `CategorizationDecisionReadService.cs` (anti-join E1 + `ILike` + LEFT JOIN `Users`); `Persistence/Postgres/CategorizationDecisionEntity.cs` + `Configurations/CategorizationDecisionConfiguration.cs` + `CategorizationDecisionRepository.cs`; `ReceiptDbContext` — `DbSet<CategorizationDecisionEntity>` (query-filter **не** ставится — журнал глобальный, доступ только через admin-API); `PrecategorizedCommodityEntity/Repository` — `original_proposed_category_id` (пишется фоном при вставке, читается в домен); запись журнала в трёх save-путях (пп. 3–5); DI (`DependencyInjectionExtensions`): recorder (scoped), read-service (scoped), репозиторий |
| Api | `Modules/CategorizationDecisions/CategorizationDecisionEndpoints.cs` — `MapCategorizationDecisionEndpoints()`, `GET /api/categorization-decisions` (права — `AdminAuthorization.AuthorizeAsync`, текстовые ошибки, `X-Total-Count`); `ReceiptCategorizationEndpoints` — парсинг аддитивных полей снимка + `IUserRepository` для admin-флага (PK-чтение один раз за запрос и только если есть хотя бы один валидный снимок); `CommodityBatchCategorizationEndpoints` — парсинг полей снимка (admin-флаг = `true`, эндпоинт admin-only); `PrecategorizedCommodityEndpoints.Confirm` — передать `UserContext.UserId!.Value`; `Program.cs` — регистрация |
| Migrations | `20261106120000_add_categorization_decisions.sql` (п. 1) |
| Frontend | `routes.ts` + `App.tsx` + `Sidebar.tsx` — `/categorization-decisions`, в навигации только `isAdmin` (стиль `/precategorized`, ADR-023/027); `api/categorizationDecisions.ts`; `hooks/useCategorizationDecisions.ts` (по образцу `usePrecategorizedCommodities`); `components/CategorizationDecisionsPage.tsx` + `CategorizationDecisionsTable.tsx` (фильтры + `Pagination` + `X-Total-Count`); `ReceiptDetails.tsx` — в `saveSelectedCategories` к каждому элементу добавлять снимок из состояния `suggestions` (`source ∈ 4` и `categoryId != null`); `useCommodityPageCategorization.ts` — в `DraftEntry` снимок `suggested { value, source }` (пишется в `applySuggestionItems`, ручные правки и `markChunkFailed` его **не** меняют; `source = existing/undefined` → снимка нет), уходить в `PUT` для changed-строк |
| Конфигурация | **Новых настроек нет** (`AGENTS.md`, `.env.example`, `docker-compose.yml` не расширяются) — журнал всегда включён, отказоустойчивость обеспечена конструкцией C1 |
| Backend (Go), бот, MongoDB, nginx | **без изменений** (весь `/api` уже проксируется) |

### 3. Точка фиксации 1 — сохранение категорий чека (FR-4)

`PUT /api/receipts/{receiptId:guid}/categories`, элемент запроса получает аддитивные
опциональные поля:

```json
{ "items": [
  { "commodityId": "3f1c…", "category": "Bakery",
    "suggestedCategory": "Dairy", "suggestionSource": "ai" }
] }
```

Правила сервера (все — без новых кодов ошибок, NFR-2/NFR-3):

1. Снимок присутствует **и валиден** (`suggestedCategory` — имя enum, `Enum.IsDefined`,
   `!= Undefined`, не число; `suggestionSource` ∈ {`ai`,`precategorized`,`cache`,
   `merchant-default`}) → после успешного применения assignment записывается событие
   `(commodity.Name → NormalizeName, source, suggested, final, принявший)`.
   `final` = сохранённая категория; пустая строка `category` (сброс) → `final = Undefined`
   (edge case задачи: «сбросил после предложения» — событие с итогом `Undefined`).
2. Снимок прислан, но невалиден / заполнен частично → **игнорируется** (`LogWarning`),
   сохранение не затронуто, события нет.
3. `commodityId` не найден в чеке (молча игнорируется сегодня) → события нет (ничего не
   подтверждено).
4. Согласия (final == suggested) пишутся наравне с исправлениями (FR-3): страница чека
   предзаполняет все позиции и сохраняет их — «оставил как предложено» и есть согласие.
   Снимки с `source = existing` (категория была у самой позиции) и `undefined` (ИИ не
   ответил) — не предложения (FR-1/FR-5b): не пишутся.
5. Реализация: `ApplyConfirmedCategoriesAsync` собирает drafts по применённым assignments
   и вызывает recorder один раз после цикла; admin-флаг эндпоинт резолвит через
   `IUserRepository.GetByIdAsync` однократно и только при наличии снимков.

### 4. Точка фиксации 2 — пакетное сохранение страницы «Товары» (FR-4)

`PUT /api/commodities/categories`, элемент `items[]` получает те же поля
`suggestedCategory`/`suggestionSource` (правила валидности/игнора — идентичны п. 3).

- События пишутся **только для строк, реально сохранённых** (вошли в `changed`: old ≠ new,
  ADR-028 C9) и имеющих валидный снимок: «согласие» на этой странице = применение
  предложенного к позиции без категории (NULL → X; proposed == final) — именно оно и
  уходит в `changed`.
- `final` — сохраняемая категория; сброс этим контрактом невозможен (assign-only) —
  событий с `final = Undefined` здесь не бывает.
- Реализация: `CommodityBatchCategorizationService.ApplyAsync` после успешного
  `UpdateCategoriesAsync` и `cache.TryAdd` — один вызов recorder по `changed` ∩ снимки
  (имена уже есть в `current`); принятый = текущий пользователь, admin-флаг `true`.
- Одиночный построчный редактор (`PUT /api/commodities/{id}/category`) — **не** точка
  фиксации: предложений там нет (FR-5b).

### 5. Точка фиксации 3 — подтверждение в `/precategorized` (FR-4, D1)

`POST /api/precategorized-commodities/{id:confirm}` после успешного применения
(outcome `Applied`): одно событие на **строку очереди** (единица — решение админа, а не
обновлённая позиция; `updatedCommodities = 0` — событие всё равно пишется, решение
состоялось):

- `commodity_name` = точное `name` строки очереди; `normalized_name` — та же нормализация;
- `suggestion_source` = `precategorized` (источник предложения для человека — очередь;
  чем наполнял строку фон — ИИ/кэш/global existing — деталь фона, ADR-027 п. 6);
- `suggested` = `original_proposed_category_id` (неизменяемый, D1), `final` =
  `proposed_category_id` на момент confirm (после правок `PUT .../category`) — правки
  админа становятся различимы фильтром «только расхождения» (FR-3);
- `accepted_by` = `UserContext.UserId` (пробрасывается в `ConfirmAsync`), admin-флаг `true`.

Отклонение строки (`DELETE`) — удаления proposals событиями (предложение не подтверждено,
итоговой категории нет). Массовый дожим мерчант-дефолтом и автоприменение при импорте —
не точки фиксации (нет человека и предложения на позицию; NFR-1).

### 6. Recorder и поведение при ошибках (NFR-3)

`ICategorizationDecisionRecorder` — единая точка записи. Контракт: **не бросает исключений
ни при каких обстоятельствах** (включая отмену токена — best-effort). Внутри:

1. Фильтрация draft'ов: источник ∈ 4; `Enum.IsDefined` обеих категорий; `SuggestedCategory
   != Undefined`; имя непустое и ≤ 256 (нормализованное — функция имени, тоже ≤ 256) —
   невалидные отбрасываются поштучно с `LogWarning` (без содержательных данных в Warning,
   только количество — прецедент ADR-028 п. 11).
2. `CreatedAt` = `TimeProvider.GetUtcNow()` (тестируемость, FakeTimeProvider).
3. Один `AddRangeAsync` (один `SaveChangesAsync`, батч multi-VALUES INSERT).
4. `catch (Exception ex) → LogError` («журнал категоризации не записан: N событий»), ответ
   не меняется.

Вызовы — строго **после** фиксации пользовательского сохранения (C1): кэш-аналогичный
порядок `TryAdd` (ADR-019/027) здесь же; crash-окно «commit → журнал» теряет события —
принятая best-effort семантика наблюдательного журнала.

Наблюдаемость: Information — число записанных событий по запросу; Warning — отброшенные
draft'ы (количество); Error — сбой записи. Названия товаров — только Debug (ПД-практика
ADR-027/028).

### 7. API чтения (FR-6, FR-7)

`GET /api/categorization-decisions` — единственный эндпоинт нового модуля, admin-only
(`AdminAuthorization.AuthorizeAsync`: пустой `UserContext.UserId` → `401`, не админ →
явный `Results.StatusCode(403)` — ADR-027/028). Стиль ошибок — текстовые тела, без
ProblemDetails.

Параметры:

| Параметр | Значение | Ошибки |
|---|---|---|
| `onlyMismatch` | `true` — только события `suggested != final` (частичный индекс) | — |
| `source` | wire-имя из 4 | иное → `400` |
| `suggestedCategory` | код категории (`Enum.IsDefined`, `!= 0`) | иначе `400` |
| `finalCategory` | код категории, `0` = «Не указана» допустим | !IsDefined → `400` |
| `search` | подстрока, `ILike` по `commodity_name` **или** `normalized_name` (прецедент очереди) | — |
| `from`, `to` | ISO 8601; `created_at >= from`, `created_at < to` (период — по событиям, см. семантику) | непарсимо → `400` |
| `limit`, `offset` | `1..200` (дефолт 50), `offset >= 0` | иначе `400` (как ADR-027 п. 7) |

Ответ `200` — массив уникальных сочетаний (дедуп E1), порядок `decidedAt DESC, id DESC`:

```json
{
  "commodityName": "Хлеб дарницкий",
  "normalizedName": "хлеб дарницкий",
  "suggestionSource": "ai",
  "suggestedCategoryId": 23, "suggestedCategoryName": "Молочные продукты",
  "finalCategoryId": 12, "finalCategoryName": "Хлеб и выпечка",
  "acceptedById": "…", "acceptedByName": "Иван",
  "acceptedByAdmin": true,
  "decidedAt": "2026-11-06T03:12:44Z"
}
```

- `X-Total-Count` — **число уникальных сочетаний** под фильтром (пагинация по строкам
  выдачи, FR-7).
- Семантика дедуп + фильтра: фильтры применяются к событиям, затем дедуп по сочетанию;
  «кто принял»/`decidedAt` — от **последнего события сочетания внутри отфильтрованного
  периода** (период суживает и «последнее»). Остальные фильтры — части ключа сочетания,
  поэтому выдача не «прыгает» между страницами.
- Счётчики/группировки отсутствуют (FR-7). Имена категорий — безопасное чтение (п. 1);
  `acceptedByName = null`, если пользователь удалён/без имени (id остаётся).

### 8. Фронтенд (FR-6)

- Маршрут `/categorization-decisions` (`routes.ts` — `categorizationDecisionsPath()`,
  ADR-023), раздел навигации `{isAdmin && ...}` (стиль `/precategorized`); прямой заход
  не-админом — «нет доступа» без запросов (сервер и так ответит `403`).
- Страница: панель фильтров (текстовый поиск, checkbox «Только расхождения», селекторы
  источника / предложенной / итоговой категории (справочник `GET /api/commodities/categories`
  + «Не указана» для итога), диапазон дат), таблица (колонки FR-6: исходное название,
  нормализованное, предложено — категория + подпись источника (переиспользуется
  `utils/categorizationSource.ts`), принято, кто принял — имя или пусто + бейдж «админ»,
  дата), `Pagination` + `X-Total-Count`; хук `useCategorizationDecisions` по образцу
  `usePrecategorizedCommodities`.
- Отправка снимков при сохранении (пп. 3–4) — незаметна пользователям: изменения только в
  payload и внутреннем состоянии черновика.
- Тестов фронта нет (`npm test`, ADR 011 E1): `npm run build`/`lint` + ручная приёмка.

### 9. Безопасность

- Чтение журнала — только админ на сервере (скрытие раздела — UX, не защита).
- Запись событий — исключительно из серверных save-путей; отдельного API для записи
  журнала **нет**; клиент предоставляет только снимок предложения (данные отображения),
  валидируемый по форме; принявший, итоговая категория, время, admin-флаг — серверные.
- Снимок не позволяет читать/менять чужие данные: события пишутся только по своим
  сохранённым позициям (owner-scoped save не меняется).
- Журнал глобален (не owner-scoped) — как очередь ADR-027; поэтому query-filter на сущность
  не ставится, а доступ к чтению закрыт admin-only.

### 10. Тестируемость

- Recorder: EF InMemory + NSubstitute; «никогда не бросает» — репозиторий-заглушка бросает,
  вызов не падает и не меняет результат сервиса; фильтрация невалидных draft'ов;
  FakeTimeProvider → детерминированный `created_at`.
- Save-пути: наличие/отсутствие/невалидность снимков → матрица «событие написано / нет»;
  регрессии существующих тестов сохранения/тостов/счётчиков не должно быть (NFR-2).
- Confirm `/precategorized`: proposed = original, final = после `PUT .../category`.
- Read-service: anti-join дедуп (два одинаковых сочетания → одна строка, «принявший» от
  последнего), фильтры и `Total` — на EF InMemory (LINQ-выражения переносимы).
- Эндпоинты: `401/403/400` исполнением `IResult` на `HttpContext` (практика ADR-027/028).
- SQL миграции и фактический план anti-join — ручная проверка на PostgreSQL (прецедент
  ADR-027: планы запросов — вне юнит-тестов).

## Явно зафиксированные семантики

| Вопрос | Решение |
|---|---|
| Единица события | Одно человеческое решение: чек/«Товары» — позиция; `/precategorized` — строка очереди (не обновлённая позиция) |
| Допустимые источники | `ai`, `precategorized`, `cache`, `merchant-default`; `existing`/`undefined` — не предложения (FR-1) |
| Кто поставил категорию без предложения | События нет (FR-5b): нет снимка — нет события |
| Предложение показали, не сохранили | События нет: журнал пишется только из save-путей (FR-5a) |
| Автоприменения (импорт с merchant-default, дожим кнопкой) | Событий нет: нет подтверждения человеком (NFR-1) |
| Одиночный редактор `PUT /api/commodities/{id}/category` | Не точка фиксации: предложений нет |
| Сброс в «Не указана» после предложения (страница чека) | Событие с `final = 0` (edge case задачи) |
| `final NULL` vs `0` в БД | Канон `0` (`Undefined`) — один «нет категории» (D11 ADR-027) |
| Ключ сочетания | `(commodity_name, suggestion_source, suggested, final)`; «Хлеб»/«ХЛЕБ» — разные строки выдачи |
| «Кто принял» и `decidedAt` | От последнего события сочетания внутри отфильтрованного периода |
| Период и дедуп | Фильтр по событиям → дедуп (последнее берётся из периода) |
| Имя принявшего | Не хранится (JOIN `users` на чтении); недоступно → пусто; id и admin-флаг — всегда |
| Admin-флаг | Снимок `users.is_admin` на момент решения |
| FK `accepted_by → users` | Осознанно нет: журнал переживает удаление пользователя |
| `commodity_id`/`receipt_id` | Не хранятся (F1); отложенный вопрос 6 |
| Невалидный снимок от клиента | Игнорируется с Warning; сохранение и коды ответа не меняются |
| Ошибка записи журнала | Логируется Error; пользовательское сохранение успешно (NFR-3); события могут потеряться |
| Порядок относительно сохранения | Строго после commit, один батч-INSERT на запрос |
| `suggestion_source` без DB-CHECK | Множество допустимого — код (recorder), а не схема |
| Новые настройки | Отсутствуют (журнал всегда включён; «выключатель» не предусмотрен) |
| Single instance | Достаточно: INSERT'ы независимы, координация не нужна (прецедент D12 ADR-027) |

## Матрица соответствия требованиям

| Требование | Решение |
|---|---|
| FR-1 | Таблица п. 1 (все поля); запись принятого из `UserContext` + снимок admin-флага (F1); имя — JOIN на чтении (п. 7) |
| FR-2 | Все четыре источника: снимок в пп. 3–4 проходит по `source ∈ 4`; `/precategorized` — `precategorized` (п. 5) |
| FR-3 | Событие пишется при любом подтверждении со снимком (согласия и исправления); различимость — фильтр `onlyMismatch` (п. 7); исправления в очереди — D1 |
| FR-4 | Три точки фиксации: п. 3 (чек), п. 4 («Товары»), п. 5 (`/precategorized`) |
| FR-5 | (а) журнал только из save-путей; (б) нет снимка — нет события; `existing`/`undefined` не предложения |
| FR-6 | П. 7 (admin-only GET, все фильтры, пагинация) + п. 8 (страница, колонки, гейт навигации) |
| FR-7 | Дедуп E1 на чтении, «принявший» от последнего события; счётчиков нет (п. 7) |
| FR-8 | Сырая таблица B1, событие на подтверждение; дедуп только на чтении |
| NFR-1 | Никаких записей в `commodities` из журнала; suggest/apply/confirm поведения не меняют; автоприменений не добавлено (пп. 3–5, «Явно зафиксированные семантики») |
| NFR-2 | Контракты строго аддитивны (опциональные поля, коды/ответы прежние); UI обычных пользователей не меняется; невалидный снимок не создаёт новых ошибок |
| NFR-3 | П. 6: после commit, один батч INSERT, recorder никогда не бросает; response-поля (`updated`) от журнала независимы |

## Последствия

### Положительные

- Появляются фактические данные об ошибках ИИ («предложено → принято») по всем трём точкам
  подтверждения — основа будущего управления качеством категоризации (следующий шаг —
  память «имя → категория», вне объёма задачи).
- Нулевое вмешательство в пользовательский поток: контракты аддитивны, поведение старым
  клиентам — прежнее; автопроставления нет по построению.
- Журнал durable, независим от жизненного цикла чеков и пользователей; единый recorder —
  одна точка эволюции (формат, ретеншен, экспорт).
- Наблюдательность изолирована: отказ журнала невидим пользователям (NFR-3), виден в логах.

### Отрицательные / компромиссы

- **Снимок предложения доверяется клиенту**: журнал фиксирует «что сообщил клиент, что
  видел»; намеренный мусор возможен в объёме собственных сохранений пользователя. Принято
  (наблюдательные данные; серверная валидация формы; путь усиления — открытый вопрос 5).
- **Best-effort запись**: crash между commit сохранения и журналом теряет события.
- **Anti-join дедуп** тяжелее простого списка; эффективность зависит от составного индекса
  и требует замера на реальных объёмах (запас — `DISTINCT ON` raw SQL, E2).
- **`/precategorized` — одно событие на строку очереди**, «массовость» подтверждения
  (тысячи позиций) в журнал не попадает — осознанно (единица наблюдения — решение).
- **История правок очереди глубиной в один шаг** (`original_proposed_category_id`):
  промежуточные `PUT .../category` до финального не видны.
- Растущая таблица без политики ретеншена (открытый вопрос 1) — риск эксплуатационный,
  не функциональный.

### Риски

| Риск | Вероятность | Митигация |
|---|---|---|
| Замедление сохранений из-за журнала | Низкая | Один батч INSERT после commit; отсутствие журнала в транзакции; метрика длительности запросов |
| Медленная дедуп-выборка на большом объёме | Средняя | Составной индекс п. 1; частичный индекс расхождений; замер планов (открытый вопрос 2); запас E2 `DISTINCT ON` |
| «Мусор» в журнале от некорректных клиентов | Низкая | Серверная валидация формы снимка; источник событий — только save-пути; усиление — открытый вопрос 5 |
| Расхождение семантики источников между журналами и `commodities.category_source` (ADR-032) | Низкая | Разные сущности: колонка — источник сохранённой категории, журнал — событие предложения; зафиксировано в «Связи с ADR» |
| Забытая колонка `original_…` при будущих правках очереди | Средняя | NOT NULL после backfill; маппинг в `MapToDomain`; тест создания строки фоном |

## Открытые вопросы

1. **Политика ретеншена** `categorization_decisions`: без автоудаления на старте; по факту
   эксплуатации выбрать архивацию/чистку (runbook, прецедент `backup.sh`) отдельным решением.
2. **Индексы по замерам**: anti-join и фильтры — замер планов на реальных объёмах;
   возможные дополнительные индексы (например, `(suggestion_source, created_at)`) —
   отдельным решением (прецедент ADR-027 «индексы отложены»).
3. **Экспорт/агрегаты** (CSV, проценты точности, матрицы путаницы) — вне объёма; путь
   эволюции поверх той же таблицы.
4. **Память «имя → подтверждённая категория»** на данных журнала (следующая вероятная
   задача) — использовать события как датасет; решений этой задачи не меняет.
5. **Усиление доверия снимка**: если на практике потребуется — серверный кеш предложений
   (A3) как аддитивная проверка согласованности клиентского снимка; сейчас YAGNI.
6. **Трассировка на позицию/чек** (`commodity_id` nullable без FK) — отложено (F1);
   добавить можно аддитивной колонкой без изменения чтений.

## Связь с другими ADR

- **ADR-009**: `PUT /api/receipts/{id}/categories` получает аддитивные опциональные поля
  снимка; поведение suggest/apply, коды, `503` при пустом `AI:BaseUrl` — без изменений
  (amend-заметка внесена в ADR-009).
- **ADR-019**: кэш в журнале не участвует; `source = cache` в событии — ярлык источника
  предложения; порядок `TryAdd` после commit переиспользуется как образец для recorder'а.
- **ADR-027**: confirm `/precategorized` дополняется записью события (после транзакции);
  очередь получает колонку `original_proposed_category_id` (статусы и транзакция confirm —
  без изменений); стиль админских эндпоинтов, `MigrationRunner`, практика «индексы — по
  замерам» переиспользуются (amend-заметка внесена в ADR-027).
- **ADR-028**: `PUT /api/commodities/categories` — аддитивные поля снимка; assign-only,
  `updated`, `409` и блокировки — без изменений; `DraftEntry` фронта получает несменяемый
  снимок предложения (amend-заметка внесена в ADR-028).
- **ADR-032**: `merchant-default` — один из четырёх источников журнала; колонка
  `commodities.category_source` (источник сохранённой категории) — отдельная сущность,
  не дублируется в журнале; автоприменение дефолта (импорт, дожим) событий не создаёт.
- **ADR-005**: справочник остаётся enum; журнал хранит коды integer + wire-имена
  источников, имена категорий не денормализует.

## Ссылки

- [Задача: Журнал решений категоризации товаров](../tasks/categorization-decision-log.md)
- Код: `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs`,
  `Api/Modules/Commodities/CommodityBatchCategorizationEndpoints.cs`,
  `Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs`,
  `Api/Modules/Users/{UserContext,AdminAuthorization}.cs`,
  `Infrastructure/Modules/Commodities/{CommodityCategorizationService,CommodityBatchCategorizationService,PrecategorizedCommodityService}.cs`,
  `Domain/Modules/Commodities/CommodityNameNormalizer.cs`,
  `frontend/src/{components/ReceiptDetails.tsx,hooks/useCommodityPageCategorization.ts,routes.ts}`
- Migrations: `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261106120000_add_categorization_decisions.sql`
- Тесты: `Analytics/tests/ReceiptCollector.Analytics.Api.Tests`
