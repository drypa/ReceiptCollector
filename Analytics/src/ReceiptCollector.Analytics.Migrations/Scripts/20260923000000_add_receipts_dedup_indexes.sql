-- Дедупликационные индексы чеков (D4).
-- Перед созданием unique-индекса удаляем возможные дубликаты по естественному ключу
-- (user_id, purchased_at, total_amount), сохраняя минимальный id (товары удаляются
-- каскадно по FK fk_commodities_receipts ON DELETE CASCADE).
-- MigrationRunner оборачивает скрипт в транзакцию целиком.

DELETE FROM receipts r USING receipts r2
WHERE r.user_id = r2.user_id
  AND r.purchased_at = r2.purchased_at
  AND r.total_amount = r2.total_amount
  AND r.id > r2.id;

CREATE UNIQUE INDEX ux_receipts_user_purchased_total ON receipts (user_id, purchased_at, total_amount);
CREATE INDEX ix_receipts_user_external ON receipts (user_id, external_id);