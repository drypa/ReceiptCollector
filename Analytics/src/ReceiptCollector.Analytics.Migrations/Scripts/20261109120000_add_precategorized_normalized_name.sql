-- Очищенный ключ строк очереди предкатегоризации: колонка normalized_name,
-- очистка очереди и backfill (ADR-034 п. 2, решения B1 и E1; FR-3, FR-4, FR-5).
-- Применяется MigrationRunner: транзакция на скрипт, учёт в migration_scripts_history,
-- поэтому повторный запуск скрипт повторно не выполняет (идемпотентность обеспечивает
-- раннер).
--
-- normalized_name — CommodityNameNormalizer.NormalizeName(name) (единственная
-- нормализация проекта, ADR-034 п. 1): индексируемый ключ поиска и дедупликации
-- очереди. Сырое name НЕ переписывается (FR-4) — оно остаётся для UI и журнала.
--
-- Зачем в скрипте SQL-копия правила и почему в pg_temp: она нужна ровно один раз —
-- для backfill исторических applied-строк. Постоянная SQL-функция запрещена решением
-- A2 (ADR-034): тесты создают схему через EnsureCreatedAsync по EF-модели, где её бы
-- не было, а runtime-нормализация всегда живёт в C#. pg_temp — временная схема сессии:
-- функция существует только в сеансе миграции и исчезает вместе с ним (шаг 9), в БД
-- после миграции ничего не остаётся. Риск дрейфа SQL ↔ C# ограничен одноразовым
-- backfill (ADR-034 п. 2 «Идемпотентность и дрейф»; паритетный тест — T-03).
--
-- Почему proposed удаляются, а applied сохраняются (FR-5, решение E1):
--   proposed — неподтверждённые предложения, собранные старым правилом; они устарели
--     и будут пересобраны фоном по новому правилу (прецедент очистки очереди —
--     20261105120000);
--   applied — история подтверждений администратора и защита от повторных предложений
--     (ADR-027 D10), поэтому сохраняются и получают backfill очищенного ключа.
--
-- Почему уникальность proposed глобальная по ключу (не по магазину): это продолжение
-- FR-3.4 ADR-027 («наименование с proposed-строкой у другого магазина не
-- предлагается»), но по очищенному ключу — один товар с кодом и без кода даёт один
-- proposed на всю очередь. applied-строки в частичный индекс не входят и могут
-- существовать у разных магазинов; для них работает прежняя уникальность
-- ux_precategorized_commodities_name_merchant (name, merchant_id).
--
-- Preflight: lower() в PostgreSQL и ToLowerInvariant() в .NET совпадают на реальном
-- корпусе названий (допущение ADR-034), но аномальное Unicode-расширение lowercase
-- не должно молча обрезаться в varchar(256) — миграция падает с явной ошибкой,
-- транзакция откатывается, данные не портятся.
--
-- ПОРЯДОК ДЕПЛОЯ (ADR-034 п. 2, обязателен): миграция меняет схему, которую старый
-- код не умеет заполнять (normalized_name NOT NULL), поэтому:
--   1. снять дамп таблицы: pg_dump -t precategorized_commodities (или ./backup.sh);
--   2. остановить Analytics: docker compose stop analytics (иначе между ADD COLUMN и
--      SET NOT NULL старый код может вставить proposed-строки с NULL — SET NOT NULL
--      упадёт и миграция откатится);
--   3. применить миграции: docker compose run --rm analytics-migrations;
--   4. запустить новый образ: docker compose up -d analytics.
-- Откат после успешной миграции простым запуском прежнего образа НЕВОЗМОЖЕН
-- (старый код не заполняет NOT NULL-колонку): восстанавливать дамп и удалять
-- колонку с индексами, либо идти forward-fix новым образом. commodities и
-- categorization_decisions эта миграция не затрагивает.

-- 1. Колонка nullable: backfill ниже, затем NOT NULL.
ALTER TABLE precategorized_commodities
    ADD COLUMN normalized_name varchar(256);

-- 2. FR-5: неподтверждённые предложения удаляются и будут пересобраны фоном.
DELETE FROM precategorized_commodities
 WHERE status = 0;

-- 3. Одноразовая SQL-копия правила (только для backfill; в runtime не используется).
--    pg_temp — временная схема сессии, после миграции функция исчезает.
CREATE FUNCTION pg_temp.normalize_commodity_name(raw_name text) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
    WITH base AS (
        SELECT regexp_replace(
                   regexp_replace(lower(raw_name), '^\s+|\s+$', '', 'g'),
                   '\s+', ' ', 'g') AS value
    ),
    cleaned AS (
        SELECT regexp_replace(
                   regexp_replace(
                       regexp_replace(value, '^(?:[\s*#№]+|[0-9]{6,})+', ''),
                       '(?:^|\s)[0-9]{6,}(?:\s+[0-9]{6,})*\s*$', ''),
                   '\s+', ' ', 'g') AS value
        FROM base
    )
    SELECT CASE
        WHEN btrim(c.value) = '' OR btrim(c.value) !~ '[A-Za-zА-Яа-яЁё]'
            THEN btrim(b.value)
        ELSE btrim(c.value)
    END
    FROM base AS b, cleaned AS c;
$$;

-- 4. Preflight: аномальное удлинение lowercase не должно молча обрезаться.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM precategorized_commodities
        WHERE status = 1
          AND length(pg_temp.normalize_commodity_name(name)) > 256
    ) THEN
        RAISE EXCEPTION
            'normalized_name exceeds varchar(256) for applied precategorized_commodities';
    END IF;
END $$;

-- 5. Backfill только applied (proposed уже удалены).
UPDATE precategorized_commodities
   SET normalized_name = pg_temp.normalize_commodity_name(name)
 WHERE status = 1
   AND normalized_name IS NULL;

-- 6. Инвариант: у каждой оставшейся строки есть ключ.
ALTER TABLE precategorized_commodities
    ALTER COLUMN normalized_name SET NOT NULL;

-- 7. Поиск по очищенному ключу (любой статус) и pre-check кандидатов.
CREATE INDEX ix_precategorized_commodities_normalized_name
    ON precategorized_commodities (normalized_name);

-- 8. Один proposed на очищенный ключ глобально (как FR-3.4 ADR-027, но по очищенному ключу).
CREATE UNIQUE INDEX ux_precategorized_commodities_normalized_proposed
    ON precategorized_commodities (normalized_name)
    WHERE status = 0;

-- 9. Временная функция больше не нужна.
DROP FUNCTION pg_temp.normalize_commodity_name(text);
