# План: исправление /login — прокси только для Telegram-клиента бота

## Ссылки
- ADR: [docs/adr/018-bot-no-proxy-internal-traffic.md](../adr/018-bot-no-proxy-internal-traffic.md)
- Задача: [docs/tasks/fix-bot-login-proxy.md](../tasks/fix-bot-login-proxy.md)

## Приоритет
`blocker`

## Цель
Устранить структурный источник бага `/login`: стандартные прокси-env переменные
(`HTTP_PROXY`/`HTTPS_PROXY`/`NO_PROXY`) больше **не передаются в контейнер бота** и
**не читаются кодом бота**. Прокси передаётся под отдельным именем `TG_PROXY_URL` и
применяется **только** к Telegram-клиенту. Внутренние HTTP-клиенты (Analytics, legacy
backend) получают явный `Transport{Proxy: nil}` (defense-in-depth).

## Принцип
> Прокси-конфигурация существует только там, где она явно объявлена.

## Порядок выполнения
Шаги 1–5 — код/конфиг в порядке зависимостей:
1. `bot/options.go` (переименование поля) → 2. `bot/bot.go` (правки ссылок на поле) →
3. `bot/analytics/client.go` (no-proxy) → 4. legacy `bot/backend` (no-proxy) →
5. `docker-compose.yml` (маппинг env).
Затем шаги 6–7 — локальные скрипты/документация (не блокируют сборку), и шаг 8 — итоговая верификация.

---

## Шаг 1 — `bot/options.go`: чтение `TG_PROXY_URL`, поле `TelegramProxyUrl`

**Файл:** `bot/options.go`

**Суть изменения:**
- Поле структуры `HttpProxyUrl string` → `TelegramProxyUrl string` (имя должно отражать,
  что прокси относится только к Telegram-клиенту).
- Читать прокси из env-переменной **`TG_PROXY_URL`** вместо `HTTP_PROXY` (Go не читает
  `TG_PROXY_URL` автоматически, `ProxyFromEnvironment` на него не смотрит).

```go
type Options struct {
	ApiToken        string
	Debug           bool
	TelegramProxyUrl string
	AnalyticsUrl    string
}

func FromEnv() Options {
	...
	proxy := getEnvVar("TG_PROXY_URL")   // НЕ HTTP_PROXY
	...
	return Options{
		...
		TelegramProxyUrl: proxy,
		...
	}
}
```

**Критерий приёма:**
- `options.go` компилируется; поле переименовано; `FromEnv()` читает `TG_PROXY_URL`.
- Пустой `TG_PROXY_URL` → прокси не используется (поведение идентично прежнему пустому `HTTP_PROXY`).

---

## Шаг 2 — `bot/bot.go`: ссылки на переименованное поле

**Файл:** `bot/bot.go` (строки 73–74)

**Суть изменения:**
Логика создания Telegram-клиента **не меняется по семантике** (прокси для Telegram API
сохраняется, это требование задачи). Меняются только две ссылки на поле:
- `options.HttpProxyUrl` → `options.TelegramProxyUrl` (в условия и в `url.Parse`).

```go
if options.TelegramProxyUrl != "" {
	proxyUrl, err := url.Parse(options.TelegramProxyUrl)
	...
	client := &http.Client{Transport: &http.Transport{Proxy: http.ProxyURL(proxyUrl)}}
	return tgbotapi.NewBotAPIWithClient(options.ApiToken, client)
}
return tgbotapi.NewBotAPI(options.ApiToken)
```

**Критерий приёма:**
- Прокси-транспорт для tgbotapi строится при непустом `TelegramProxyUrl` (как раньше для `HttpProxyUrl`).
- `go build ./...` в `bot/` проходит.

---

## Шаг 3 — `bot/analytics/client.go`: явный транспорт без прокси

**Файл:** `bot/analytics/client.go` (функция `NewClient`, строки 31–40)

**Суть изменения:**
Analytics — всегда внутренний сервис (`analytics:5039` в Docker-сети), прокси не нужен.
Добавить явный `Transport{Proxy: nil}` в `http.Client`. Это defense-in-depth: даже при
ручном проставлении `HTTP_PROXY` в контейнер analytics продолжит работать.

```go
func NewClient(baseURL string) *Client {
	return &Client{
		BaseURL: baseURL,
		HTTPClient: &http.Client{
			// Внутренний сервис: прямой доступ, минуя прокси.
			Transport: &http.Transport{
				Proxy: nil,
			},
			Timeout: 10 * time.Second,
		},
		MaxRetries: 3,
		RetryDelay: 1 * time.Second,
	}
}
```

**Критерий приёма:**
- `NewClient` возвращает клиент с `Transport.Proxy == nil`.
- Это чинит корень бага `/login` (путь Bot → Analytics по HTTP, команда `/login`).
- `go build ./...` в `bot/` проходит.

---

## Шаг 4 — legacy `bot/backend`: явный no-proxy клиент

**Файлы:** `bot/backend/client.go`, `bot/backend/user.go`, `bot/backend/receipt.go`

**Контекст:** это мёртвый код (не вызывается из `main.go` — используется `GrpcClient` и
`user.Provider`), но он должен остаться безопасным. Защита на двух уровнях: в проде `HTTP_PROXY`
отсутствует в контейнере; локально — явный клиент с `Proxy: nil` внутри пакета (защищает запуск
бинарника на машине разработчика, где `HTTP_PROXY` задан в shell).

**Суть изменений:**
1. `bot/backend/client.go` — в структуру `Client` добавить поле `httpClient *http.Client`,
   в `New()` инициализировать `&http.Client{Transport: &http.Transport{Proxy: nil}}`:
   ```go
   type Client struct {
       backendUrl string
       httpClient *http.Client
   }

   func New(backendUrl string) Client {
       return Client{
           backendUrl: backendUrl,
           httpClient: &http.Client{Transport: &http.Transport{Proxy: nil}},
       }
   }
   ```
   (добавить импорт `net/http`).
2. `bot/backend/user.go` — заменить `http.Post(registerUrl, ...)` на
   `client.httpClient.Post(registerUrl, ...)`; заменить `http.Get(getLinkUrl)` на
   `client.httpClient.Get(getLinkUrl)`.
3. `bot/backend/receipt.go` — заменить `http.Post(addReceiptUrl, ...)` на
   `client.httpClient.Post(addReceiptUrl, ...)`.

**Критерий приёма:**
- В пакете `bot/backend` не осталось прямых вызовов `http.Get`/`http.Post` (поиск не находит
  `http.Get(` и `http.Post(` в `user.go`/`receipt.go`).
- `go build ./...` и `go vet ./...` в `bot/` проходят (код dead, но должен компилироваться).

---

## Шаг 5 — `docker-compose.yml`: маппинг env для сервиса `bot`

**Файл:** `docker-compose.yml` (сервис `bot`, строка 77)

**Суть изменения:**
Переменная прокси берётся из `.env` под тем же именем `TG_PROXY_URL`, что и в контейнере:
```yaml
- TG_PROXY_URL=${TG_PROXY_URL}
```

В `environment` сервиса `bot` **не должно остаться** `HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`.
После этого `ProxyFromEnvironment` в контейнере ничего не находит и возвращает `nil` → любой код
бота (включая будущий и legacy) никогда не ходит через прокси по умолчанию.

**Важно:** в `.env` задаётся имя `TG_PROXY_URL` (вместо прежнего `HTTP_PROXY`) — оно же
передаётся в контейнер; имя `HTTP_PROXY` для бота не используется ни в `.env`, ни в контейнере.

**Критерий приёма:**
- В `docker-compose.yml` в блоке `environment` сервиса `bot` нет `HTTP_PROXY`/`HTTPS_PROXY`/`NO_PROXY`;
  есть `TG_PROXY_URL=${TG_PROXY_URL}`.

---

## Шаг 6 — `dev-run.sh` (локальный запуск): экспорт `TG_PROXY_URL`

**Файл:** `dev-run.sh` (строка 264)

**Суть изменения:**
Экспорт имени `TG_PROXY_URL` (значение берётся из `.env`, где оно задаётся под тем же именем).
Строки `export HTTP_PROXY` в скрипте **удаляются** — для бота больше не используется:

```sh
export TG_PROXY_URL="${TG_PROXY_URL:-}"
```

**Критерий приёма:**
- При локальном запуске бинарника бота переменная `TG_PROXY_URL` становится в окружении;
  значение равно `TG_PROXY_URL` из `.env`.

---

## Шаг 7 — Документация: `HTTP_PROXY` (опция бота) → `TG_PROXY_URL`

**Файлы (указаны в ADR-018, раздел «Сопутствующие»):**
- `.env.example` — переменная `TG_PROXY_URL=` вместо `HTTP_PROXY=`, обновить комментарий.
- `README.md` (строка 138) — заменить упоминание `HTTP_PROXY` как опции бота на `TG_PROXY_URL`.
- `docs/plans/dev-debug-run-script.md` (строки 85, 369) — заменить `HTTP_PROXY` (опция бота) на `TG_PROXY_URL`.
- `docs/tasks/dev-debug-run-script.md` (строки 27, 75, 131) — то же самое.

**Не трогать:** упоминания `HTTP_PROXY` в ADR-012/016 и `docs/plans/completed/analytics-production-deployment.md`
(исторические/сторонние контексты — вне рамок этой задачи).

**Критерий приёма:**
- В документации не остаётся упоминаний `HTTP_PROXY` как опции/env-переменной **бота**;
  используется `TG_PROXY_URL`.
- Понятно, что значение берётся из `.env`-переменной `TG_PROXY_URL` напрямую.

---

## Шаг 8 — Итоговая верификация

**Команды (по порядку):**
```bash
cd bot
go build ./...      # сборка всех пакетов бота
go vet ./...        # статический анализ
go test ./...       # юнит-тесты
cd ..
docker compose build bot   # сборка образа бота в CI-стиле
```

**Ручные проверки (по чек-листу ADR-018):**
1. `docker compose build bot && docker compose up -d bot` → `/login` возвращает ссылку на авторизацию
   (в логах нет ошибок Squid).
2. Telegram-сообщения бота доставляются (трафик к Telegram API идёт через Squid):
   - при `TG_PROXY_URL` с невалидным адресом бот не стартует / не отвечает;
   - при валидном — работает.
3. `TG_PROXY_URL` пустой в `.env` → работа без прокси, поведение не меняется.
4. Остальные команды (`/register`, `/confirm`, `/report`, `/add_receipt`) работают как раньше
   (gRPC-пути не изменены).

**Критерий приёма (конечный):**
- Все критерии задачи `fix-bot-login-proxy.md` выполнены: `/login` возвращает ссылку; внутренние
  сервисы доступны напрямую минуя прокси; трафик к Telegram API идёт через Squid; ошибки Squid в
  ответах `/login` исчезли.

---

## Сводка по файлам

| Шаг | Файл | Суть |
|-----|------|------|
| 1 | `bot/options.go` | чтение `TG_PROXY_URL`, поле `TelegramProxyUrl` |
| 2 | `bot/bot.go` | ссылки на переименованное поле (логика Telegram-прокси не меняется) |
| 3 | `bot/analytics/client.go` | `Transport: &http.Transport{Proxy: nil}` |
| 4 | `bot/backend/client.go`, `user.go`, `receipt.go` | явный no-proxy `http.Client` в legacy-коде |
| 5 | `docker-compose.yml` | `TG_PROXY_URL=${TG_PROXY_URL}`, убрать `HTTP_PROXY/HTTPS_PROXY/NO_PROXY` |
| 6 | `dev-run.sh` | экспорт `TG_PROXY_URL` |
| 7 | `.env.example`, `README.md`, `docs/plans/dev-debug-run-script.md`, `docs/tasks/dev-debug-run-script.md` | обновление упоминаний `HTTP_PROXY` → `TG_PROXY_URL` |
| 8 | — | верификация: `go build`, `go vet`, `go test`, `docker compose build bot` |
