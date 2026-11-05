-- Колонки для категории товаров по умолчанию у магазина (ADR-032, п. 1 — решения A1 и B1).
--
-- merchants.default_commodity_category_id integer NULL — код CommodityCategory,
-- заданный администратором как «категория товаров по умолчанию» для магазина.
-- FK не используется: справочник товарных категорий — enum в коде (ADR 005, B1),
-- таблицы-справочника нет (прецедент precategorized_commodities.proposed_category_id,
-- ADR-027 п. 3). CHECK запрещает только 0 (Undefined) как дефолт:
-- валидация кода по enum'у — на записи в эндпоинте (T-07), при чтении — «безопасное»
-- отображение в домен (невалидный код читается как «не задан», T-02).
--
-- commodities.category_source varchar(32) NULL — persisted-источник присвоения
-- категории (ADR-032, п. 1, решение B1). Единственное значение на сегодня —
-- 'merchant-default' (записывается только при автоприменении дефолта при импорте
-- чека и при дожиме, T-04/T-05); все прочие пути записи категории оставляют NULL.
--
-- Backfill исторических строк НЕ выполняется: NULL = manual/legacy (осознанно, B1).
-- Индексы НЕ добавляются: дожим опирается на существующие ix_receipts_merchant_id
-- и ix_commodities_receipt_id (отложенное решение — прецедент ADR-027 п. 3).
--
-- Развёртываемость (rolling deploy): обе колонки nullable и аддитивны — старый код
-- работает с новой схемой, новый код со старой схемой (до применения миграции) — тоже.
--
-- Раннер обеспечивает идемпотентность: транзакция на скрипт, учёт в
-- migration_scripts_history — повторный прогон скрипт повторно не выполняет.

ALTER TABLE merchants
    ADD COLUMN default_commodity_category_id integer NULL,
    ADD CONSTRAINT ck_merchants_default_commodity_category
        CHECK (default_commodity_category_id IS NULL
               OR default_commodity_category_id <> 0);  -- Undefined нельзя задать дефолтом

ALTER TABLE commodities
    ADD COLUMN category_source varchar(32) NULL;
