-- Ключевые слова категоризации (ADR-035 п. 1, решения A1 и B1): boost- и exclude-списки
-- на категорию. Одна строка = одно слово в одном списке одной категории.
-- Применяется MigrationRunner: транзакция на скрипт, учёт в migration_scripts_history,
-- повторный запуск скрипта не выполняется.
--
-- Почему CHECK на list_type: множество типов закрыто требованием FR-1 («два списка»:
-- boost — «вероятно эта категория», exclude — «скорее не эта категория»). В отличие от
-- suggestion_source журнала (ADR-033), где множество открытое и CHECK сознательно не ставился,
-- здесь констрейнт защищает от опечаток в ручном SQL и делает схему самодокументируемой.
-- Добавление третьего типа — продуктовое изменение, правка констрейнта — одна строка.
--
-- Почему нет FK на категорию: справочника категорий в БД нет (ADR-005) — CommodityCategory
-- это enum в коде, имена и группы считаются CommodityCategoryHelper на чтении (прецеденты
-- precategorized_commodities.proposed_category_id ADR-027, categorization_decisions ADR-033).
-- Undefined (0) запрещён CHECK: слова «Не указана» не существует, Other (255) допустим.
--
-- Почему лимит 200 слов на список не в схеме (ADR-035 A5): триггеры в проекте не используются,
-- лимит — продуктовое правило, а не целостность данных; проверка выполняется сервисом
-- (count перед вставкой). Гонка может дать единичный перелёт, что безвредно (ADR-019).
--
-- Почему сид одноразовый (ADR-035 B1): скрипт выполняется ровно один раз (migration_scripts_history),
-- поэтому удалённое администратором слово не воскресает ни при рестарте, ни при деплое.
-- ON CONFLICT DO NOTHING — страховка ручного/частичного повторного выполнения того же скрипта
-- (и случая, когда два слова блока нормализуются в один ключ). Маркера «сид» у строк нет:
-- атрибут «сид» не нужен ни одному сценарию (B3 отклонён).
--
-- Слово хранится как введено (FR-5), кроме обрамляющих пробелов; normalized_word — ключ
-- case-insensitive уникальности и вход подстрочного матчинга (FR-6):
-- trim + lowercase + collapse пробелов (CommodityCategoryKeywordRules.NormalizeWord).
-- CommodityNameNormalizer к словам НЕ применяется (ADR-035, «Явно зафиксированные семантики»).

CREATE TABLE commodity_category_keywords
(
    id              uuid        PRIMARY KEY,
    category_id     integer     NOT NULL,
    list_type       varchar(16) NOT NULL,
    word            varchar(64) NOT NULL,
    normalized_word varchar(64) NOT NULL,
    CONSTRAINT ck_commodity_category_keywords_category_not_undefined
        CHECK (category_id <> 0),
    CONSTRAINT ck_commodity_category_keywords_list_type
        CHECK (list_type IN ('boost', 'exclude')),
    CONSTRAINT ck_commodity_category_keywords_word_length
        CHECK (char_length(word) BETWEEN 2 AND 64)
);

-- Дубликат в рамках «одна категория + один список» (регистр не важен) — FR-5.
-- Индекс же обслуживает per-category чтение (префикс category_id, list_type).
CREATE UNIQUE INDEX ux_commodity_category_keywords_category_list_word
    ON commodity_category_keywords (category_id, list_type, normalized_word);

-- Сид: содержимое блока «Разграничения схожих категорий» (ADR-022) из BuildPayload (FR-2,
-- ADR-035 п. 2). Раскладка строк блока на слова:
--   1) слова субъекта → boost категорий, которые они называют;
--   2) те же слова → exclude каждой категории-цели из «не …» (UC-5: исключение сильнее boost);
--   3) перечисления в скобках — положительное определение категории-цели → её boost;
--   4) словоформы приведены к форме, входящей подстрокой в исходную форму (единственное число);
--      смысловые дубли в одну категорию не размножаются.
-- Маркеры seed:begin/seed:end — только для воспроизведения блока в тесте идемпотентности
-- и эксплуатационного восстановления удалённых сид-слов; на выполнение скрипта не влияют.
--
-- seed:begin
INSERT INTO commodity_category_keywords (id, category_id, list_type, word, normalized_word) VALUES
    -- Сухофрукты (DriedFruits = 42): изюм, курага, чернослив, финики — не Fruits (свежие фрукты/ягоды)
    (gen_random_uuid(), 42, 'boost',   'изюм',      'изюм'),
    (gen_random_uuid(), 42, 'boost',   'курага',    'курага'),
    (gen_random_uuid(), 42, 'boost',   'чернослив', 'чернослив'),
    (gen_random_uuid(), 42, 'boost',   'финик',     'финик'),
    (gen_random_uuid(), 26, 'boost',   'фрукты',    'фрукты'),
    (gen_random_uuid(), 26, 'boost',   'ягоды',     'ягоды'),
    (gen_random_uuid(), 26, 'exclude', 'изюм',      'изюм'),
    (gen_random_uuid(), 26, 'exclude', 'курага',    'курага'),
    (gen_random_uuid(), 26, 'exclude', 'чернослив', 'чернослив'),
    (gen_random_uuid(), 26, 'exclude', 'финик',     'финик'),

    -- Соусы (Sauces = 43) и Приправы (Spices = 44): кетчуп, майонез, соевый соус, специи —
    -- не Groceries (бакалея — крупы, мука, макароны, сахар, соль, масло, чай/кофе)
    (gen_random_uuid(), 43, 'boost',   'кетчуп',      'кетчуп'),
    (gen_random_uuid(), 43, 'boost',   'майонез',     'майонез'),
    (gen_random_uuid(), 43, 'boost',   'соевый соус', 'соевый соус'),
    (gen_random_uuid(), 44, 'boost',   'специи',      'специи'),
    (gen_random_uuid(), 19, 'boost',   'крупы',       'крупы'),
    (gen_random_uuid(), 19, 'boost',   'мука',        'мука'),
    (gen_random_uuid(), 19, 'boost',   'макароны',    'макароны'),
    (gen_random_uuid(), 19, 'boost',   'сахар',       'сахар'),
    (gen_random_uuid(), 19, 'boost',   'соль',        'соль'),
    (gen_random_uuid(), 19, 'boost',   'масло',       'масло'),
    (gen_random_uuid(), 19, 'boost',   'чай',         'чай'),
    (gen_random_uuid(), 19, 'boost',   'кофе',        'кофе'),
    (gen_random_uuid(), 19, 'exclude', 'кетчуп',      'кетчуп'),
    (gen_random_uuid(), 19, 'exclude', 'майонез',     'майонез'),
    (gen_random_uuid(), 19, 'exclude', 'соевый соус', 'соевый соус'),
    (gen_random_uuid(), 19, 'exclude', 'специи',      'специи'),

    -- Колбасные изделия (Sausages = 45): колбаса, сосиски, сардельки, ветчина —
    -- не Meat (свежее/замороженное мясо)
    (gen_random_uuid(), 45, 'boost',   'колбаса',   'колбаса'),
    (gen_random_uuid(), 45, 'boost',   'сосиски',   'сосиски'),
    (gen_random_uuid(), 45, 'boost',   'сардельки', 'сардельки'),
    (gen_random_uuid(), 45, 'boost',   'ветчина',   'ветчина'),
    (gen_random_uuid(), 20, 'boost',   'мясо',      'мясо'),
    (gen_random_uuid(), 20, 'exclude', 'колбаса',   'колбаса'),
    (gen_random_uuid(), 20, 'exclude', 'сосиски',   'сосиски'),
    (gen_random_uuid(), 20, 'exclude', 'сардельки', 'сардельки'),
    (gen_random_uuid(), 20, 'exclude', 'ветчина',   'ветчина'),

    -- Конфеты (Sweets = 46): конфеты, шоколад, мармелад, зефир — не Confectionery
    -- (печенье, вафли, пряники, галеты, крекеры, торты, пирожные, выпечка, кексы)
    (gen_random_uuid(), 46, 'boost',   'конфеты',   'конфеты'),
    (gen_random_uuid(), 46, 'boost',   'шоколад',   'шоколад'),
    (gen_random_uuid(), 46, 'boost',   'мармелад',  'мармелад'),
    (gen_random_uuid(), 46, 'boost',   'зефир',     'зефир'),
    (gen_random_uuid(), 28, 'boost',   'печенье',   'печенье'),
    (gen_random_uuid(), 28, 'boost',   'вафли',     'вафли'),
    (gen_random_uuid(), 28, 'boost',   'пряники',   'пряники'),
    (gen_random_uuid(), 28, 'boost',   'галеты',    'галеты'),
    (gen_random_uuid(), 28, 'boost',   'крекеры',   'крекеры'),
    (gen_random_uuid(), 28, 'boost',   'торты',     'торты'),
    (gen_random_uuid(), 28, 'boost',   'пирожные',  'пирожные'),
    (gen_random_uuid(), 28, 'boost',   'выпечка',   'выпечка'),
    (gen_random_uuid(), 28, 'boost',   'кексы',     'кексы'),
    (gen_random_uuid(), 28, 'exclude', 'конфеты',   'конфеты'),
    (gen_random_uuid(), 28, 'exclude', 'шоколад',   'шоколад'),
    (gen_random_uuid(), 28, 'exclude', 'мармелад',  'мармелад'),
    (gen_random_uuid(), 28, 'exclude', 'зефир',     'зефир'),

    -- Консервы (CannedFood = 47): рыбные, мясные, овощные консервы, паштеты, джемы и варенья
    -- (в банках) — не Groceries
    (gen_random_uuid(), 47, 'boost',   'консервы', 'консервы'),
    (gen_random_uuid(), 47, 'boost',   'паштет',   'паштет'),
    (gen_random_uuid(), 47, 'boost',   'джем',     'джем'),
    (gen_random_uuid(), 47, 'boost',   'варенье',  'варенье'),
    (gen_random_uuid(), 19, 'exclude', 'консервы', 'консервы'),
    (gen_random_uuid(), 19, 'exclude', 'паштет',   'паштет'),
    (gen_random_uuid(), 19, 'exclude', 'джем',     'джем'),
    (gen_random_uuid(), 19, 'exclude', 'варенье',  'варенье'),

    -- Бытовая техника (HomeAppliances = 50): холодильники, стиральные машины, чайники,
    -- микроволновые печи, пылесосы — не Electronics (смартфоны, компьютеры, планшеты, ТВ, гаджеты)
    (gen_random_uuid(), 50, 'boost',   'холодильник',       'холодильник'),
    (gen_random_uuid(), 50, 'boost',   'стиральная машина', 'стиральная машина'),
    (gen_random_uuid(), 50, 'boost',   'чайник',            'чайник'),
    (gen_random_uuid(), 50, 'boost',   'микроволновая печь', 'микроволновая печь'),
    (gen_random_uuid(), 50, 'boost',   'пылесос',           'пылесос'),
    (gen_random_uuid(), 3,  'boost',   'смартфон',          'смартфон'),
    (gen_random_uuid(), 3,  'boost',   'компьютер',         'компьютер'),
    (gen_random_uuid(), 3,  'boost',   'планшет',           'планшет'),
    (gen_random_uuid(), 3,  'boost',   'телевизор',         'телевизор'),
    (gen_random_uuid(), 3,  'boost',   'гаджет',            'гаджет'),
    (gen_random_uuid(), 3,  'exclude', 'холодильник',       'холодильник'),
    (gen_random_uuid(), 3,  'exclude', 'стиральная машина', 'стиральная машина'),
    (gen_random_uuid(), 3,  'exclude', 'чайник',            'чайник'),
    (gen_random_uuid(), 3,  'exclude', 'микроволновая печь', 'микроволновая печь'),
    (gen_random_uuid(), 3,  'exclude', 'пылесос',           'пылесос'),

    -- Мебель (Furniture = 49): стулья, столы, шкафы, диваны, кровати —
    -- не HomeGoods (посуда, текстиль, хранение, интерьер)
    (gen_random_uuid(), 49, 'boost',   'стул',     'стул'),
    (gen_random_uuid(), 49, 'boost',   'стол',     'стол'),
    (gen_random_uuid(), 49, 'boost',   'шкаф',     'шкаф'),
    (gen_random_uuid(), 49, 'boost',   'диван',    'диван'),
    (gen_random_uuid(), 49, 'boost',   'кровать',  'кровать'),
    (gen_random_uuid(), 10, 'boost',   'посуда',   'посуда'),
    (gen_random_uuid(), 10, 'boost',   'текстиль', 'текстиль'),
    (gen_random_uuid(), 10, 'boost',   'хранение', 'хранение'),
    (gen_random_uuid(), 10, 'boost',   'интерьер', 'интерьер'),
    (gen_random_uuid(), 10, 'exclude', 'стул',     'стул'),
    (gen_random_uuid(), 10, 'exclude', 'стол',     'стол'),
    (gen_random_uuid(), 10, 'exclude', 'шкаф',     'шкаф'),
    (gen_random_uuid(), 10, 'exclude', 'диван',    'диван'),
    (gen_random_uuid(), 10, 'exclude', 'кровать',  'кровать'),

    -- Бытовая химия (HouseholdChemicals = 53): чистящие и моющие средства, стиральные порошки,
    -- средства для мытья посуды и посудомоечных машин, освежители воздуха, губки и перчатки для
    -- уборки, бумажные полотенца, мусорные пакеты, средства от насекомых —
    -- не HomeGoods и не CosmeticsAndHygiene (мыло, шампуни, зубная паста)
    (gen_random_uuid(), 53, 'boost',   'чистящее средство',          'чистящее средство'),
    (gen_random_uuid(), 53, 'boost',   'моющее средство',            'моющее средство'),
    (gen_random_uuid(), 53, 'boost',   'стиральный порошок',         'стиральный порошок'),
    (gen_random_uuid(), 53, 'boost',   'средство для мытья посуды',  'средство для мытья посуды'),
    (gen_random_uuid(), 53, 'boost',   'освежитель воздуха',         'освежитель воздуха'),
    (gen_random_uuid(), 53, 'boost',   'губка',                      'губка'),
    (gen_random_uuid(), 53, 'boost',   'перчатки для уборки',        'перчатки для уборки'),
    (gen_random_uuid(), 53, 'boost',   'бумажные полотенца',         'бумажные полотенца'),
    (gen_random_uuid(), 53, 'boost',   'мусорные пакеты',            'мусорные пакеты'),
    (gen_random_uuid(), 53, 'boost',   'средство от насекомых',      'средство от насекомых'),
    (gen_random_uuid(), 4,  'boost',   'мыло',                       'мыло'),
    (gen_random_uuid(), 4,  'boost',   'шампунь',                    'шампунь'),
    (gen_random_uuid(), 4,  'boost',   'зубная паста',               'зубная паста'),
    (gen_random_uuid(), 10, 'exclude', 'чистящее средство',          'чистящее средство'),
    (gen_random_uuid(), 10, 'exclude', 'моющее средство',            'моющее средство'),
    (gen_random_uuid(), 10, 'exclude', 'стиральный порошок',         'стиральный порошок'),
    (gen_random_uuid(), 10, 'exclude', 'средство для мытья посуды',  'средство для мытья посуды'),
    (gen_random_uuid(), 10, 'exclude', 'освежитель воздуха',         'освежитель воздуха'),
    (gen_random_uuid(), 10, 'exclude', 'губка',                      'губка'),
    (gen_random_uuid(), 10, 'exclude', 'перчатки для уборки',        'перчатки для уборки'),
    (gen_random_uuid(), 10, 'exclude', 'бумажные полотенца',         'бумажные полотенца'),
    (gen_random_uuid(), 10, 'exclude', 'мусорные пакеты',            'мусорные пакеты'),
    (gen_random_uuid(), 10, 'exclude', 'средство от насекомых',      'средство от насекомых'),
    (gen_random_uuid(), 4,  'exclude', 'чистящее средство',          'чистящее средство'),
    (gen_random_uuid(), 4,  'exclude', 'моющее средство',            'моющее средство'),
    (gen_random_uuid(), 4,  'exclude', 'стиральный порошок',         'стиральный порошок'),
    (gen_random_uuid(), 4,  'exclude', 'средство для мытья посуды',  'средство для мытья посуды'),
    (gen_random_uuid(), 4,  'exclude', 'освежитель воздуха',         'освежитель воздуха'),
    (gen_random_uuid(), 4,  'exclude', 'губка',                      'губка'),
    (gen_random_uuid(), 4,  'exclude', 'перчатки для уборки',        'перчатки для уборки'),
    (gen_random_uuid(), 4,  'exclude', 'бумажные полотенца',         'бумажные полотенца'),
    (gen_random_uuid(), 4,  'exclude', 'мусорные пакеты',            'мусорные пакеты'),
    (gen_random_uuid(), 4,  'exclude', 'средство от насекомых',      'средство от насекомых'),

    -- Товары для рукоделия (CraftSupplies = 48): пряжа, ткань, бисер, фурнитура,
    -- наборы для творчества — не StationeryAndBooks (бумага, ручки, офисные принадлежности)
    (gen_random_uuid(), 48, 'boost',   'пряжа',                  'пряжа'),
    (gen_random_uuid(), 48, 'boost',   'ткань',                  'ткань'),
    (gen_random_uuid(), 48, 'boost',   'бисер',                  'бисер'),
    (gen_random_uuid(), 48, 'boost',   'фурнитура',              'фурнитура'),
    (gen_random_uuid(), 48, 'boost',   'набор для творчества',   'набор для творчества'),
    (gen_random_uuid(), 8,  'boost',   'бумага',                 'бумага'),
    (gen_random_uuid(), 8,  'boost',   'ручка',                  'ручка'),
    (gen_random_uuid(), 8,  'boost',   'офисные принадлежности', 'офисные принадлежности'),
    (gen_random_uuid(), 8,  'exclude', 'пряжа',                  'пряжа'),
    (gen_random_uuid(), 8,  'exclude', 'ткань',                  'ткань'),
    (gen_random_uuid(), 8,  'exclude', 'бисер',                  'бисер'),
    (gen_random_uuid(), 8,  'exclude', 'фурнитура',              'фурнитура'),
    (gen_random_uuid(), 8,  'exclude', 'набор для творчества',   'набор для творчества'),

    -- Снеки (Snacks = 51): чипсы, сухарики, орешки, попкорн, снековые смеси —
    -- не Groceries и не Sweets (конфеты, шоколад)
    (gen_random_uuid(), 51, 'boost',   'чипсы',          'чипсы'),
    (gen_random_uuid(), 51, 'boost',   'сухарики',       'сухарики'),
    (gen_random_uuid(), 51, 'boost',   'орешки',         'орешки'),
    (gen_random_uuid(), 51, 'boost',   'попкорн',        'попкорн'),
    (gen_random_uuid(), 51, 'boost',   'снековая смесь', 'снековая смесь'),
    (gen_random_uuid(), 19, 'exclude', 'чипсы',          'чипсы'),
    (gen_random_uuid(), 19, 'exclude', 'сухарики',       'сухарики'),
    (gen_random_uuid(), 19, 'exclude', 'орешки',         'орешки'),
    (gen_random_uuid(), 19, 'exclude', 'попкорн',        'попкорн'),
    (gen_random_uuid(), 19, 'exclude', 'снековая смесь', 'снековая смесь'),
    (gen_random_uuid(), 46, 'exclude', 'чипсы',          'чипсы'),
    (gen_random_uuid(), 46, 'exclude', 'сухарики',       'сухарики'),
    (gen_random_uuid(), 46, 'exclude', 'орешки',         'орешки'),
    (gen_random_uuid(), 46, 'exclude', 'попкорн',        'попкорн'),
    (gen_random_uuid(), 46, 'exclude', 'снековая смесь', 'снековая смесь'),

    -- Фитнес и спорт (FitnessAndSports = 52): абонементы в фитнес-клубы и бассейны,
    -- спортивные секции — не SportingGoods (инвентарь и экипировка) и не Entertainment
    -- (кино, концерты, парки)
    (gen_random_uuid(), 52, 'boost',   'абонемент',         'абонемент'),
    (gen_random_uuid(), 52, 'boost',   'фитнес-клуб',       'фитнес-клуб'),
    (gen_random_uuid(), 52, 'boost',   'бассейн',           'бассейн'),
    (gen_random_uuid(), 52, 'boost',   'спортивная секция', 'спортивная секция'),
    (gen_random_uuid(), 6,  'boost',   'инвентарь',         'инвентарь'),
    (gen_random_uuid(), 6,  'boost',   'экипировка',        'экипировка'),
    (gen_random_uuid(), 41, 'boost',   'кино',              'кино'),
    (gen_random_uuid(), 41, 'boost',   'концерт',           'концерт'),
    (gen_random_uuid(), 41, 'boost',   'парк',              'парк'),
    (gen_random_uuid(), 6,  'exclude', 'абонемент',         'абонемент'),
    (gen_random_uuid(), 6,  'exclude', 'фитнес-клуб',       'фитнес-клуб'),
    (gen_random_uuid(), 6,  'exclude', 'бассейн',           'бассейн'),
    (gen_random_uuid(), 6,  'exclude', 'спортивная секция', 'спортивная секция'),
    (gen_random_uuid(), 41, 'exclude', 'абонемент',         'абонемент'),
    (gen_random_uuid(), 41, 'exclude', 'фитнес-клуб',       'фитнес-клуб'),
    (gen_random_uuid(), 41, 'exclude', 'бассейн',           'бассейн'),
    (gen_random_uuid(), 41, 'exclude', 'спортивная секция', 'спортивная секция')
ON CONFLICT DO NOTHING;
-- seed:end
