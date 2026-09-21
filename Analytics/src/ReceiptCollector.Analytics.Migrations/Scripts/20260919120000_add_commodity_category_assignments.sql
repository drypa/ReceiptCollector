BEGIN;

CREATE TABLE commodity_category_assignments
(
    id uuid PRIMARY KEY,
    normalized_name varchar(256) NOT NULL,
    name varchar(256) NOT NULL,
    category_id integer NOT NULL,
    category_name varchar(128) NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE UNIQUE INDEX ux_commodity_category_assignments_normalized_name ON commodity_category_assignments (normalized_name);