# Проброс переменных окружения из .env в контейнеры (docker-compose.yml)

## Приоритет
high

## Проблема
В `.env.example` описаны переменные окружения для доступа к ИИ (`AI__*`,
`CommodityCategoryCache__MaxSize`), но в `docker-compose.yml` они не передаются
в сервис `analytics`. Аналогично:
- `BOT_DEBUG` описан в `.env.example`, читается кодом бота, но не пробрасывается
  в сервис `bot`;
- `MONGO_URL`, `GET_RECEIPT_WORKER_INTERVAL`, `NALOGRU_BASE_ADDR` захардкожены
  в `docker-compose.yml` и не могут быть переопределены из `.env`.

Это ограничивает конфигурируемость прод-запуска через `.env`.

## Цель
Все релевантные переменные, описанные в `.env.example`, должны пробрасываться в
соответствующие сервисы через `docker-compose.yml` (prod). Значения по умолчанию
для collector сохраняются через `:-` (не ломать текущее прод-поведение при пустом
`.env`).

## Пользователи
Разработчики/операторы, деплоящие систему через `docker compose` с конфигурацией
из `.env`.

## Сценарий использования
1. Оператор заполняет `.env` по шаблону `.env.example`, указывая AI-настройки
   (`AI__BaseUrl` и т.д.), `BOT_DEBUG`, при необходимости переопределяя
   `MONGO_URL` / `GET_RECEIPT_WORKER_INTERVAL` / `NALOGRU_BASE_ADDR`.
2. `docker compose up` стартует стек.
3. Сервис `analytics` получает AI-переменные в контейнер; если `AI__BaseUrl`
   пуст — AI-категоризация отключена (503), как и задумано.
4. Сервис `bot` получает `BOT_DEBUG` (включается/выключается дебаг-логирование).
5. Сервис `collector` использует значения из `.env` или прод-дефолты.

## Критерии приёмки
- [ ] В `docker-compose.yml`, сервис `analytics`, в секции `environment` добавлен
      проброс (типизированные поля — с дефолтами, равными прод-базису HEAD
      `appsettings.json`; решение согласовано, см. ADR-021 — без дефолтов
      пустые строки ломают старт .NET-биндинга для TimeSpan/int):
      - `AI__BaseUrl=${AI__BaseUrl}` (без дефолта: пусто = AI отключён, 503)
      - `AI__Model=${AI__Model:-qwen}`
      - `AI__Timeout=${AI__Timeout:-00:00:10}`
      - `AI__Concurrency=${AI__Concurrency:-3}`
      - `AI__ApiKey=${AI__ApiKey}` (без дефолта: пусто = без Authorization)
      - `CommodityCategoryCache__MaxSize=${CommodityCategoryCache__MaxSize:-1000}`
- [ ] В `docker-compose.yml`, сервис `bot`, в секции `environment` добавлен
      проброс `BOT_DEBUG=${BOT_DEBUG}` (без дефолта — безопасно).
- [ ] В `docker-compose.yml`, сервис `collector`, `MONGO_URL`,
      `GET_RECEIPT_WORKER_INTERVAL`, `NALOGRU_BASE_ADDR` переведены с хардкода
      на проброс с дефолтами, сохраняющими текущие прод-значения:
      - `MONGO_URL=${MONGO_URL:-mongodb://mongo:27017}`
      - `GET_RECEIPT_WORKER_INTERVAL=${GET_RECEIPT_WORKER_INTERVAL:-15m}`
      - `NALOGRU_BASE_ADDR=${NALOGRU_BASE_ADDR:-https://irkkt-mobile.nalog.ru:8888}`
- [ ] В `.env.example` значение `MONGO_URL` заменено с
      `mongodb://localhost:27017` на контейнерное `mongodb://mongo:27017`
      (с комментарием про хост-запуск) — иначе оператор, скопировавший шаблон,
      сломает collector в контейнере.
- [ ] `docker-compose.develop.yml` не изменён.
- [ ] «Контейнерные» адреса бота (`BACKEND_GRPC_ADDR`, `REPORTS_GRPC_ADDR`,
      `ANALYTICS_URL`) и пути volume (`RAW_TICKET_DUMP_PATH`,
      `GET_TICKET_ERROR_PATH`, `SSL_CERTS_PATH`, `TEMPLATES_PATH`) не изменяются.

## Edge cases
- Если в `.env` `AI__BaseUrl` пуст/не задан — контейнер `analytics` получает
  пустое значение, AI-категоризация отключена (503), сервис продолжает работу.
- Если в `.env` не заданы типизированные `AI__*`/`CommodityCategoryCache__MaxSize`
  — действуют `:-`-дефолты (прод-базис); пустые строки не попадают в контейнер,
  старт analytics не падает.
- Если в `.env` collector-переменные не заданы — действуют прод-дефолты через
  `:-` (текущее поведение прод без изменений). Особенно важно для
  `GET_RECEIPT_WORKER_INTERVAL`: пустая строка иначе молча ушла бы в
  dev-fallback 1 минута (см. `workers/settings.go`).
- Пустой `AI__Model` при включённом AI (BaseUrl задан) недопустим — защищено
  дефолтом `:-qwen`.

## Зависимости
- Нет внешних зависимостей. Изменения только в `docker-compose.yml`
  и `.env.example`. Код сервисов не меняется.

## Мета
- Автор: user
- Дата создания: 2026-09-23
- Статус: Draft