BEGIN;

ALTER TABLE users
    ADD COLUMN telegram_id integer;

UPDATE users
SET telegram_id = 0
WHERE telegram_id IS NULL;

ALTER TABLE users
    ALTER COLUMN telegram_id SET NOT NULL;
