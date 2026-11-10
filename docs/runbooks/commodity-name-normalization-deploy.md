# Инструкция по деплою: очищенный ключ названий в очереди предкатегоризации

> **Кому предназначен этот документ.** Оператору, применяющему релиз Analytics с
> нормализацией названий товаров (миграция
> `20261109120000_add_precategorized_normalized_name.sql`) на dev или продакшен
> сервере ReceiptCollector. Знание внутренней архитектуры не требуется —
> достаточно уметь работать с командной строкой Linux и Docker.
>
> **Что делаем.** Короткая остановка Analytics → дамп таблицы очереди →
> применение SQL-миграции → запуск нового образа. Миграция добавляет в
> `precategorized_commodities` колонку `normalized_name NOT NULL` с двумя
> индексами, удаляет неподтверждённые предложения `proposed` (они будут
> пересобраны фоном по новому правилу) и заполняет ключ у строк `applied`.
>
> **Сколько займёт.** 5–15 минут простоя Analytics (таблица очереди — тысячи
> строк, backfill и создание индексов быстрые).
>
> **Главное правило.** Порядок шагов **обязателен** (см. «Почему порядок
> именно такой»). Перед каждым шагом, помеченным ✅, сверяйте фактический
> вывод с ожидаемым; при расхождении, помеченном ⛔, — не продолжайте,
> см. раздел «Откат».
>
> Архитектурное обоснование: [ADR-034 п. 2](../adr/034-commodity-name-normalization.md)
> («Схема PostgreSQL и миграция», «Порядок деплоя», «Откат»).
> Декомпозиция и контрольные запросы:
> [план, разделы «Порядок применения миграции» и «Ответ на открытый вопрос №6»](../plans/commodity-name-normalization.md).

---

## Почему порядок именно такой

Миграция переходит колонкой в `NOT NULL`, а **старый** код Analytics эту
колонку заполнять не умеет. Отсюда окно риска:

- если запустить миграцию **при работающем старом Analytics**, между
  `ADD COLUMN` и `SET NOT NULL` старый код может вставить `proposed`-строку с
  `normalized_name = NULL` — тогда `SET NOT NULL` упадёт и миграция
  откатится (данные не портятся, но деплой придётся повторить);
- если **после** успешной миграции запустить старый образ — его вставки в
  очередь будут падать на NOT NULL, фон «зашумится» Warning'ами (данные тоже
  не портятся, но сервис деградирует).

Поэтому: **дамп → остановка Analytics → миграции → новый образ**.
Дополнительно: образ Analytics должен быть **собран из кода, который пишет
`normalized_name`** (коммиты T-02…T-08 фичи), до запуска миграций.

`commodities` и `categorization_decisions` миграция **не меняет** — сырые
имена и накопленная история журнала не затрагиваются.

---

## Обозначения и соглашения

| Обозначение | Значение |
|---|---|
| `<REPO>` | Корень проекта на сервере (уточните через `pwd`). |
| `.env` | Файл настроек в корне: `PG_LOGIN`, `PG_SECRET`, `MONGO_BACKUP`. |
| `receipt-postgres` / `receipt-analytics` / `receipt-analytics-migrations` | Имена контейнеров (docker-compose.yml). |
| ✅ | Ожидаемый результат — проверьте перед продолжением. |
| ⛔ | Точка принятия решения: при отклонении — стоп, см. «Откат». |

Все команды — из корня проекта. В начале сессии:

```bash
cd <REPO>
set -a; source .env; set +a
```

✅ `echo "$PG_LOGIN"` выводит имя пользователя PostgreSQL.

---

## Шаг 0. Подготовка (без простоя)

### 0.1. Новый образ собран и содержит нужные коммиты

```bash
./build.sh
docker images drypa/receipt-collector-analytics
```

✅ Образ свежей сборки. Убедитесь у инженера, что образ собран из коммита,
содержащего всю фичу `commodity-name-normalization` — задачи T-01…T-08 в
порядке таблицы «Ход выполнения» плана (диапазон коммитов фичи: от коммита
нормализатора до коммита регресса журнала; состав файлов каждой задачи —
в сводках `docs/reviews/commodity-name-normalization/`).

⛔ Образ старый или коммитов нет — **миграцию не применять**: после `SET NOT
NULL` такой код не сможет писать в очередь.

### 0.2. Сервисы работают штатно

```bash
docker ps --format 'table {{.Names}}\t{{.Status}}'
```

✅ Есть `receipt-postgres` (healthy), `receipt-analytics` (Up),
`receipt-mongo` (healthy).

### 0.3. Зафиксируйте счётчики «до» (понадобятся в контрольном запросе 3)

```bash
docker exec "$(docker ps -f name=receipt-postgres -q)" \
  psql -U "$PG_LOGIN" -d receipts -c \
  "SELECT status, count(*) FROM precategorized_commodities GROUP BY status ORDER BY status;"
```

✅ Вывод вида `0 | <N_proposed>` и/или `1 | <N_applied>`. **Запишите оба
числа.** Пустая таблица (пустой вывод) — допустима: миграция безопасна на
пустой таблице.

---

## Шаг 1. Дамп таблицы очереди (страховка для отката)

Таблица небольшая (тысячи строк), достаточно дампа одной таблицы. Вариант А:

```bash
mkdir -p "$MONGO_BACKUP/normalization-pre-deploy"
docker exec "$(docker ps -f name=receipt-postgres -q)" \
  pg_dump --format=custom -U "$PG_LOGIN" -t precategorized_commodities receipts \
  > "$MONGO_BACKUP/normalization-pre-deploy/precategorized_commodities.dump"
ls -lh "$MONGO_BACKUP/normalization-pre-deploy/precategorized_commodities.dump"
```

✅ Файл существует и существенно больше нуля.

Вариант Б (полный бэкап обеих Mongo-баз и всей базы Analytics —
`analytics-postgres.dump`): `./backup.sh`.

⛔ Дамп не получился (файл нулевой/ошибка) — **продолжать нельзя**: без дампа
откат после миграции невозможен.

---

## Шаг 2. Остановить Analytics

```bash
docker compose -p receipt-collector stop analytics
```

✅ `Stopping receipt-analytics ... Done`. Проверка:
`docker ps --filter name=receipt-analytics` — пусто.

Альтернатива (если нужно сохранить веб-UI): выставить
`Precategorization__Enabled=false` и перезапустить контейнер — фон остановится,
но для одного инстанса проще остановить сервис целиком.

⛔ Контейнер не остановился — `docker stop receipt-analytics` вручную.
Убедитесь, что остановлен именно контейнер Analytics: фон — единственный
источник записей в очередь.

---

## Шаг 3. Применить миграции

```bash
docker compose -p receipt-collector run --rm analytics-migrations
```

✅ Контейнер завершился с кодом 0 (`EXIT 0`); в выводе — применение скрипта
`20261109120000_add_precategorized_normalized_name.sql`. Повторный запуск
безопасен: `MigrationRunner` учитывает скрипты в `migration_scripts_history`
и повторно их не выполняет.

> Примечание: `run --rm` самоудаляет контейнер после завершения, поэтому
> `Exited (0)` у `receipt-analytics-migrations` в `docker ps -a` может
> оставаться от прежнего `up`. Достоверная проверка применения миграции —
> строка в `migration_scripts_history` (см. «Частые проблемы», №4).

⛔ Ошибка применения (например, `normalized_name exceeds varchar(256)` —
preflight аномального lowercase-расширения, или NOT NULL violation от
работавшего старого кода) — транзакция скрипта откатана целиком, схема и
данные не изменены. Устраните причину (шаг 2 не пропущен? образ из шага 0?) и
повторите шаг 3, либо [откатитесь](#откат) запуском прежнего образа.

---

## Шаг 4. Запустить новый образ и проверить

```bash
docker compose -p receipt-collector up -d analytics
docker ps --format 'table {{.Names}}\t{{.Status}}'
```

✅ `receipt-analytics` — `Up (healthy)`; `receipt-analytics-migrations` —
`Exited (0)` (разовая задача, это норма).

```bash
docker logs receipt-analytics --tail 50 | grep -iE 'error|fail'
```

✅ Ошибок старта/записи в очередь нет.

---

## Контрольные запросы после миграции (обязательно)

Источник: план, раздел «Ответ на открытый вопрос №6 ADR» (постоянный набор).
Выполняются в `psql` контейнера `receipt-postgres`, база `receipts`:

```bash
docker exec -it "$(docker ps -f name=receipt-postgres -q)" \
  psql -U "$PG_LOGIN" -d receipts
```

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

Ожидаемые результаты:

| № | Ожидание |
|---|---|
| 1 | `0` |
| 2 | `0` (появление `status = 0` позже — фон уже начал работу, это норма) |
| 3 | строки `status = 0` нет; `status = 1` **не меньше** числа с шага 0.3 |
| 4 | `0`. Легитимное исключение — fallback-строки, имя которых состоит только из цифр ≥6 (например `2417268000006`): по ADR-034 п. 1 они сохраняют код в ключе и матчатся regex'ом; при count > 0 просмотрите возвращённые строки (тот же запрос без count) до решения об остановке |
| 5 | `пирог с вишней по ре`, `туалетная бумага "сваё" (3сл, 4р)`, `рж.край хлеб нар.300г` (для имён, реально присутствующих в базе) |
| 6 | `normalized_name character varying(256)`, Nullable `not null`; индексы `ix_precategorized_commodities_normalized_name` и `ux_precategorized_commodities_normalized_proposed UNIQUE ... WHERE status = 0`; прежние `ux_precategorized_commodities_name_merchant`, `ix_precategorized_commodities_status` |

⛔ Любое расхождение — остановите Analytics (`docker compose -p
receipt-collector stop analytics`), не оставляйте сервис работать с
подозрительной схемой, привлекайте инженера (набор запросов также покрывает
постоянный тест `PrecategorizedNormalizedNameMigrationTests`).

Фоновое заполнение очереди начнётся по расписанию `Precategorization:*`
(рабочее окно ADR-030): через некоторое время `status = 0` снова станет
ненулевым — **это ожидаемо**, новые `proposed` собраны по очищенному ключу.

---

## Откат <a name="откат"></a>

### До применения миграции (шаг 3 не выполнялся или упал)

Схема не менялась (скрипт транзакционен, при ошибке всё откатано). Достаточно
запустить прежний образ:

```bash
docker compose -p receipt-collector up -d analytics
```

### После успешной миграции (шаг 3 завершился кодом 0)

⚠️ **Простой откат образа невозможен**: старый код не заполняет NOT NULL
колонку `normalized_name`, его вставки в очередь будут падать. Выберите один
из вариантов.

**Вариант А (рекомендуемый) — forward-fix.** Оставьте новую схему, выпустите
исправление новым образом и примените его поверх. Данных не теряется;
`commodities` и `categorization_decisions` миграция не меняла.

**Вариант Б — полный откат схемы через дамп.** Выполняйте только по решению
инженера:

1. Остановить Analytics:
   `docker compose -p receipt-collector stop analytics`.
2. Восстановить таблицу из дампа шага 1 (дамп вернёт и удалённые `proposed` —
   напоминание: их удаление было намеренным по FR-5):

   ```bash
   docker exec -i "$(docker ps -f name=receipt-postgres -q)" \
     pg_restore -U "$PG_LOGIN" -d receipts --clean --if-exists --no-owner \
     < "$MONGO_BACKUP/normalization-pre-deploy/precategorized_commodities.dump"
   ```

   Если дампа нет — удалить изменения вручную (порядок обратный миграции):

   ```sql
   DROP INDEX IF EXISTS ux_precategorized_commodities_normalized_proposed;
   DROP INDEX IF EXISTS ix_precategorized_commodities_normalized_name;
   ALTER TABLE precategorized_commodities DROP COLUMN IF EXISTS normalized_name;
   ```

3. **Удалить запись из истории миграций**, иначе при следующем деплое раннер
   пропустит скрипт и новый код упадёт на отсутствующей колонке:

   ```sql
   DELETE FROM migration_scripts_history
    WHERE script_name LIKE '20261109120000%';
   ```

4. Запустить прежний образ: `docker compose -p receipt-collector up -d analytics`.

---

## Чек-лист финальных проверок

- [ ] `receipt-analytics` — `Up (healthy)`, в логах нет ошибок записи очереди;
- [ ] `receipt-analytics-migrations` завершён кодом 0, а скрипт `20261109120000_add_precategorized_normalized_name.sql` присутствует в `migration_scripts_history` (см. «Частые проблемы», №4);
- [ ] контрольные запросы 1–6 дали ожидаемые результаты (раздел выше);
- [ ] число `status = 1` не меньше дорелизного (шаг 0.3 ↔ запрос 3);
- [ ] страница `/precategorized` открывается, сырые имена отображаются как раньше;
- [ ] дамп `$MONGO_BACKUP/normalization-pre-deploy/precategorized_commodities.dump`
      сохранён до окончания наблюдения.

---

## Частые проблемы

**1. `SET NOT NULL` / NOT NULL violation при применении миграции.**
Значит шаг 2 был пропущен — старый фон успел вставить `proposed` с `NULL`.
Скрипт откатился целиком. Остановите Analytics (шаг 2) и повторите шаг 3.

**2. `normalized_name exceeds varchar(256)` (preflight).**
Аномальное расширение lowercase у экзотических имён в `applied`. Транзакция
откатана, данные целы. Привлеките инженера: нужно решить по ADR-034
(расширение колонки — отдельное решение).

**3. После деплоя очередь пустая.**
Фон работает в рабочем окне `Precategorization__WorkWindow*` (ADR-030) и после
периода бездействия `IdleTimeout`. Это не ошибка: `proposed` пересоберутся в
окне. Проверить активность фона: `docker logs receipt-analytics | grep -i precategor`.

**4. Сомнение «а миграция точно применилась?»**
`SELECT script_name FROM migration_scripts_history ORDER BY applied_on DESC LIMIT 3;` —
должен быть `20261109120000_add_precategorized_normalized_name.sql`.
