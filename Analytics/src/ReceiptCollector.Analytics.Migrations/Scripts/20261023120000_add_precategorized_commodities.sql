-- Очередь предкатегоризации товаров (ADR-027 п.3, C3, D2, D9, FR-4.1).
-- Применяется MigrationRunner: транзакция на скрипт, учёт в migration_scripts_history,
-- поэтому повторный запуск скрипт повторно не выполняет (идемпотентность обеспечивается раннером).

CREATE TABLE precategorized_commodities
(
    id                   uuid PRIMARY KEY,
    name                 varchar(256) NOT NULL,           -- точное наименование, как commodities.name
    merchant_id          uuid         NOT NULL,           -- магазин для контекста админу
    proposed_category_id integer      NOT NULL,           -- CommodityCategory, != 0 (Undefined)
    status               smallint     NOT NULL DEFAULT 0, -- 0 = proposed, 1 = applied
    created_at           timestamptz  NOT NULL DEFAULT now(),
    applied_at           timestamptz,
    CONSTRAINT fk_precategorized_commodities_merchants
        FOREIGN KEY (merchant_id) REFERENCES merchants (id) ON DELETE CASCADE,
    CONSTRAINT ck_precategorized_commodities_status CHECK (status IN (0, 1))
);

-- Уникальность пары (точное наименование, магазин) — C3.
CREATE UNIQUE INDEX ux_precategorized_commodities_name_merchant
    ON precategorized_commodities (name, merchant_id);

-- Индекс по статусу — выборка очереди админа.
CREATE INDEX ix_precategorized_commodities_status
    ON precategorized_commodities (status, created_at);

-- Индекс на commodities (name, category_id) НЕ создаётся в этой задаче
-- (решение PM, NFR-6): состав индексов под выборку кандидатов заранее не определён —
-- нужна DISTINCT-выборка (name, merchant_id), а merchant у commodity есть
-- только через join commodities → receipts → merchants. Текущее ожидание:
-- запрос кандидатов пойдёт Seq Scan'ом по отфильтрованному множеству.
-- По результатам эксплуатации замерить планы запросов и решить применимость
-- индексов отдельным решением (см. «Риски» п. 6 и ADR-027).
