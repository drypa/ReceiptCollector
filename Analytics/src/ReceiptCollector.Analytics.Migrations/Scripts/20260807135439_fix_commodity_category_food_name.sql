BEGIN;

-- Идемпотентный data-fix: переименование отображаемого имени Food в denormalized category_name.
-- category_id не трогаем (код 1 сохраняется). Повторный запуск не меняет данные (WHERE-условие).
UPDATE commodities
SET category_name = 'Прочая еда'
WHERE category_id = 1 AND category_name = 'Продукты питания';

