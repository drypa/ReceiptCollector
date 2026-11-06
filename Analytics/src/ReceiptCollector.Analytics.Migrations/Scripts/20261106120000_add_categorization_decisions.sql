-- Журнал решений категоризации «предложено → принято» (ADR-033 п. 1, решения B1 и D1).
-- Применяется MigrationRunner: транзакция на скрипт, учёт в migration_scripts_history,
-- поэтому повторный запуск скрипт повторно не выполняет (идемпотентность обеспечивается раннером).
--
-- categorization_decisions — сырые события журнала, по событию на каждое подтверждение
-- человеком предложенной категории (FR-8). Дедупликация — только на чтении (ADR-033 п. 7),
-- в таблице её нет. Черновики событий пишет исключительно серверный recorder
-- (ADR-033 п. 6); отдельного API записи журнала не существует (ADR-033 п. 9).
--
-- Поля:
--   commodity_name        — исходное название товара, как в чеке (входит в ключ сочетания:
--                           «Хлеб» и «ХЛЕБ» остаются разными строками выдачи);
--   normalized_name       — CommodityNameNormalizer.NormalizeName (единственная нормализация
--                           проекта, ADR-019/027);
--   suggestion_source     — wire-имя источника предложения: ai | precategorized | cache |
--                           merchant-default. CHECK по перечню осознанно НЕ ставится:
--                           допустимое множество — свойство кода (enum CategorizationSource,
--                           проверка в recorder'е), расширение источников не должно требовать
--                           миграции (ADR-033 п. 1);
--   suggested_category_id — код CommodityCategory; 0 (Undefined) запрещён: предложения
--                           «Не указана» не бывает (FR-5b);
--   final_category_id     — итоговая категория; 0 = Undefined («Не указана», сброс на
--                           странице чека) — один канонический «нет категории» (D11 ADR-027);
--   accepted_by           — users.id принявшего решение. FK осознанно НЕТ (ADR-033 п. 1, F1):
--                           CASCADE терял бы исторические события при удалении пользователя,
--                           RESTRICT блокировал бы удаление; имя пользователя не
--                           денормализуется и подтягивается JOIN'ом на чтении;
--   accepted_by_admin     — снимок users.is_admin на момент решения (роль может измениться);
--   created_at            — время события, проставляется сервером (TimeProvider в recorder'е).
--
-- Коды категорий — integer без FK: справочник категорий — enum в коде (ADR 005), таблицы-
-- справочника нет (прецеденты precategorized_commodities.proposed_category_id ADR-027,
-- commodities.category_source ADR-032). Отображаемые имена категорий не денормализуются
-- (безопасное чтение: Enum.IsDefined + CommodityCategoryHelper.GetDisplayName).
--
-- Дополнение очереди предкатегоризации (ADR-033 п. 1, решение D1):
--   precategorized_commodities.original_proposed_category_id — первоначальное предложение,
--   записываемое только фоном при создании строки. PUT /api/precategorized-commodities/{id}/category
--   перезаписывает proposed_category_id, но эту колонку не трогает, поэтому confirm пишет
--   в журнал «предложено = original, принято = текущий proposed_category_id» и правки админа
--   становятся различимы фильтром «только расхождения» (ADR-033 п. 5, FR-3).
--
-- Backfill исторических строк = текущий proposed_category_id: историю правок задним числом
-- восстановить невозможно (предложения ранее не сохранялись), журнал начинается с момента
-- внедрения. NOT NULL после backfill: у каждой строки очереди обязано быть исходное
-- предложение, иначе confirm не сможет записать корректное событие.
--
-- Развёртываемость (rolling deploy): после SET NOT NULL старая версия кода (до T-02) ещё не
-- пишет новую колонку при вставке строк очереди, поэтому миграция на dev/прод применяется
-- после готовности T-02 либо при временно выключенном фоне Precategorization__Enabled=false
-- (ограничение порядка зафиксировано в декомпозиции T-01).

CREATE TABLE categorization_decisions
(
    id                    uuid PRIMARY KEY,
    commodity_name        varchar(256) NOT NULL,
    normalized_name       varchar(256) NOT NULL,
    suggestion_source     varchar(32)  NOT NULL,
    suggested_category_id integer      NOT NULL,
    final_category_id     integer      NOT NULL,
    accepted_by           uuid         NOT NULL,
    accepted_by_admin     boolean      NOT NULL,
    created_at            timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ck_categorization_decisions_suggested_not_undefined
        CHECK (suggested_category_id <> 0),
    CONSTRAINT ck_categorization_decisions_final_non_negative
        CHECK (final_category_id >= 0)
);

-- Дедуп-выборка (E1, greatest-n-per-group по сочетанию) и группировки сочетаний:
-- ключ сочетания + порядок «самое свежее первым» (created_at DESC, id DESC).
CREATE INDEX ix_categorization_decisions_combination
    ON categorization_decisions
       (commodity_name, suggestion_source, suggested_category_id, final_category_id, created_at DESC, id DESC);

-- Порядок списка по умолчанию, фильтр периода, будущая ретенция (ADR-033, открытый вопрос 1).
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
