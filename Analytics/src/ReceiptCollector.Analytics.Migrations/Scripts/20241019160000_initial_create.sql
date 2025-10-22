BEGIN;

CREATE TABLE users
(
    id uuid PRIMARY KEY,
    name varchar(256) NOT NULL,
    external_id varchar(128) NOT NULL
);

CREATE UNIQUE INDEX ux_users_external_id ON users (external_id);

CREATE TABLE merchants
(
    id uuid PRIMARY KEY,
    name varchar(256) NOT NULL,
    category integer NOT NULL,
    address varchar(512),
    inn varchar(16)
);

CREATE UNIQUE INDEX ux_merchants_inn ON merchants (inn);

CREATE TABLE receipts
(
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL,
    merchant_id uuid NOT NULL,
    external_id varchar(128) NOT NULL,
    total_amount numeric(18,2) NOT NULL,
    purchased_at timestamptz NOT NULL,
    CONSTRAINT fk_receipts_users FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_receipts_merchants FOREIGN KEY (merchant_id) REFERENCES merchants (id) ON DELETE RESTRICT
);

CREATE TABLE commodities
(
    id uuid PRIMARY KEY,
    receipt_id uuid NOT NULL,
    name varchar(256) NOT NULL,
    quantity numeric(18,3) NOT NULL,
    unit_price numeric(18,2) NOT NULL,
    nds integer NOT NULL,
    nds_sum numeric(18,2) NOT NULL,
    category_id integer,
    category_name varchar(128),
    CONSTRAINT fk_commodities_receipts FOREIGN KEY (receipt_id) REFERENCES receipts (id) ON DELETE CASCADE
);

CREATE INDEX ix_receipts_user_id ON receipts (user_id);
CREATE INDEX ix_receipts_merchant_id ON receipts (merchant_id);
CREATE INDEX ix_commodities_receipt_id ON commodities (receipt_id);

