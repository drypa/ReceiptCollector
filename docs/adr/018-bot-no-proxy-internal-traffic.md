# 18. Явная настройка прокси только для Telegram-клиента бота

## Статус
Принято (переработано; первоначальное решение на `NO_PROXY` отклонено пользователем)

Причина переработки (требование пользователя дословно): *«мне не нравится решение через NO_PROXY — нужно чтобы HTTP_PROXY использовалось не на все соединения внутри bot, а только для доступа к telegram. Для доступа к внутренним сервисам должен использоваться отдельный http.Client»*.

## Контекст
Команда `/login` в Telegram-боте не работает: пользователь получает ошибку Squid-прокси вместо ссылки на авторизацию в Analytics.

### Источник бага (корень)
`bot/analytics/client.go` создаёт `http.Client` без явного `Transport` → Go использует `http.DefaultTransport` → его `Proxy: ProxyFromEnvironment` автоматически читает стандартные переменные `HTTP_PROXY`/`HTTPS_PROXY` из окружения контейнера → **весь** исходящий HTTP процесса, включая запросы к внутренним сервисам, идёт через Squid. Squid не может достучаться до внутренних DNS-имён Docker-сети (`analytics`, `collector`) → `/login` падает.

Корень проблемы — не «отсутствие `NO_PROXY`», а сам принцип: **прокси-конфигурация через стандартные env-переменные разливается на весь процесс** (`http.DefaultTransport`/`http.DefaultClient` подхватывают её неявно). Лечится это не списком исключений (`NO_PROXY`), а устранением неявного чтения: прокси должен существовать только там, где явно объявлен.

### Коммуникационные пути бота

| Путь | Транспорт | Прокси? |
|---|---|---|
| Bot → Telegram API | HTTP, кастомный `Transport{Proxy: http.ProxyURL(...)}` в `bot/bot.go:80` | Да, явно, корректно |
| Bot → Analytics (`/api/users/auth/link`) | HTTP, `http.DefaultTransport` (`bot/analytics/client.go:34-36`) | Да, неявно через env — **БАГ** |
| Bot → Backend (gRPC) | gRPC/HTTP2 | Не затронут — свой транспорт |
| Bot → Reports (gRPC) | gRPC/HTTP2 | Не затронут |

Единственный сломанный путь — Bot → Analytics по HTTP. Затронутые команды: `/login` (остальные используют gRPC).

### Legacy-код
`bot/backend/user.go` и `bot/backend/receipt.go` используют `http.Get`/`http.Post` через `http.DefaultClient` (тоже уважает `HTTP_PROXY`). Это мёртвый код (`main.go` использует только `NewGrpcClient`), но он **должен остаться безопасным** — не ходить через прокси.

## Решение
Принцип: **прокси-конфигурация существует только там, где она явно объявлена.**

- Стандартные `HTTP_PROXY` / `HTTPS_PROXY` / `NO_PROXY` **не передаются в контейнер бота и не читаются кодом бота** — устраняется источник неявного прокси на уровне процесса.
- Значение прокси передаётся в контейнер под **отдельным именем** `TG_PROXY_URL` (Go не читает его автоматически, `ProxyFromEnvironment` на него не смотрит).
- Каждый HTTP-клиент бота явно объявляет свою прокси-политику:
  - **Telegram API** — явный `Transport{Proxy: http.ProxyURL(...)}` (сохраняется как есть);
  - **внутренние сервисы (Analytics, legacy backend)** — явный `Transport{Proxy: nil}` (напрямую).

### 1. docker-compose.yml

В сервисе `bot` переменная прокси берётся из `.env` напрямую:

```yaml
- TG_PROXY_URL=${TG_PROXY_URL}
```

В `environment` сервиса `bot` **не должно остаться** `HTTP_PROXY`, `HTTPS_PROXY` и `NO_PROXY`. После этого `ProxyFromEnvironment` в контейнере ничего не находит и возвращает `nil` → `http.DefaultTransport`/`http.DefaultClient` любого кода бота (включая будущий и legacy) никогда не ходят через прокси.

Значение Squid задаётся в `.env` под именем `TG_PROXY_URL` и тем же именем передаётся в контейнер (compose-подстановка `${TG_PROXY_URL}`). Имя `HTTP_PROXY` для бота не используется вовсе — ни в `.env`, ни в контейнере.

### 2. bot/options.go

Читать прокси из `TG_PROXY_URL` вместо `HTTP_PROXY`. Поле структуры переименовать в `TelegramProxyUrl` (из `HttpProxyUrl`) — опция относится только к Telegram-клиенту, имя должно это отражать:

```go
type Options struct {
	ApiToken        string
	Debug           bool
	TelegramProxyUrl string
	AnalyticsUrl    string
}

func FromEnv() Options {
	...
	proxy := os.Getenv("TG_PROXY_URL")   // не HTTP_PROXY!
	...
}
```

Пустой `TG_PROXY_URL` → прокси не используется (то же поведение, что и при пустом `HTTP_PROXY` раньше).

### 3. bot/bot.go

Логика создания Telegram-клиента **сохраняется без изменения семантики**: при непустом `TelegramProxyUrl` строится `http.Client{Transport: &http.Transport{Proxy: http.ProxyURL(proxyUrl)}}` для tgbotapi. Прокси для Telegram API не отключается (требование задачи). Технически меняется только имя поля в двух ссылках (`bot.go:73-74`).

### 4. bot/analytics/client.go

Явный транспорт без прокси — аналитика это всегда внутренний сервис (`analytics:5039` в Docker-сети):

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

Это defense-in-depth: даже если кто-то позже вручную проставит `HTTP_PROXY` в контейнер, analytics продолжит работать.

### 5. Legacy `bot/backend/*.go` (client.go, user.go, receipt.go)

Мёртвый код остаётся безопасным на двух уровнях:
- **в проде** — автоматически: `HTTP_PROXY` отсутствует в контейнере, `http.DefaultClient` не знает о прокси;
- **defense-in-depth / локально** — явный no-proxy клиент в самом пакете: `http.Get`/`http.Post` заменяются на вызовы клиента с `Transport{Proxy: nil}`. Это защищает и случай запуска бинарника бота на машине разработчика, где `HTTP_PROXY` задан в shell.

Изменение минимально и не меняет поведение (код не вызывается из `main.go`):
- `client.go`: в структуру `Client` добавить поле `httpClient *http.Client`, в `New()` инициализировать `&http.Client{Transport: &http.Transport{Proxy: nil}}`;
- `user.go`, `receipt.go`: заменить `http.Post`/`http.Get` на методы поля `httpClient`.

## Последствия

### Положительные
- **Источник бага устранён структурно**: стандартных прокси-env в контейнере нет → неявное «разливание» прокси по процессу исключено;
- Прокси для Telegram сохранён и теперь ограничен единственным явным местом;
- Внутренние клиенты ходят напрямую и не зависят от окружения;
- Не нужен `NO_PROXY` и его обслуживание (нет списка хостов);
- Локальный запуск бота на машине с `HTTP_PROXY` в shell больше не «ломает» внутренние вызовы — раньше был источником проблем;
- Если позже понадобится прокси для Telegram — меняется только значение `TG_PROXY_URL`, без влияния на внутренний трафик.

### Отрицательные
- Кастомное имя `TG_PROXY_URL`: разработчик должен помнить, что для бота используется оно, а не старое `HTTP_PROXY` (для сервисов, которым прокси действительно нужен, применяйте `TG_PROXY_URL`);
- Каждый новый `http.Client` в боте обязан явно задавать `Transport` — нужна дисциплина (механизм «эксплицитный транспорт по умолчанию» отсутствует в stdlib). При росте числа клиентов стоит ввести общий хелпер `newNoProxyClient()` в отдельном пакете;
- Правки в нескольких местах (compose, options.go, оба клиента, legacy) — объём больше single-line-фикса, но каждое изменение маленькое и локальное.

### Нейтральные
- Legacy-код затрагивается, хотя не исполняется — поведение не меняется;
- `.env`: имя переменной меняется на `TG_PROXY_URL` (одноразовое изменение конфигурации пользователем).

## Альтернативы

1. **`NO_PROXY` в docker-compose (прежний вариант ADR-018)** — отклонено пользователем: оставляет процесс-глобальное чтение `HTTP_PROXY`, «лечит симптомы» списком хостов и требует его поддержки.
2. **Оставить `HTTP_PROXY` в контейнере + добавить `Proxy: nil` только в analytics-клиент** — чинит `/login`, но legacy `http.DefaultClient` и любые будущие клиенты остаются под неявным прокси; источник бага не устранён. Отклонено.
3. **Глобальная замена `http.DefaultTransport = &http.Transport{Proxy: nil}` в `main.go`** — мутация глобального состояния `net/http`, влияет на весь процесс и на тесты, скрывает реальные настройки. Отклонено.
4. **Доработка Squid (резолвинг внутренних DNS и ACL)** — усложнение инфраструктуры ради маршрутизации, которая не нужна вовсе; прокси не должен знать о внутренней сети. Отклонено.
5. **Только переименование env (`TG_PROXY_URL`) без явных транспортов внутренних клиентов** — рабочий минимум в проде, но нет defense-in-depth для локального запуска и legacy-кода. Отклонено в пользу полного решения.

## Затронутые файлы (код/конфиг)
- `docker-compose.yml` — `TG_PROXY_URL=${TG_PROXY_URL}` в сервисе `bot` (вместо прежнего `HTTP_PROXY=${HTTP_PROXY}`);
- `bot/options.go` — чтение `TG_PROXY_URL`, поле `TelegramProxyUrl`;
- `bot/bot.go` — только ссылки на переименованное поле (логика прокси для Telegram не меняется);
- `bot/analytics/client.go` — `Transport: &http.Transport{Proxy: nil}`;
- `bot/backend/client.go`, `bot/backend/user.go`, `bot/backend/receipt.go` — явный no-proxy клиент в legacy-коде.

### Сопутствующие (документация/скрипты локального запуска)
- `dev-run.sh` — экспорт `TG_PROXY_URL="${TG_PROXY_URL:-}"` (без `HTTP_PROXY`);
- `.env.example` — переменная `TG_PROXY_URL=` вместо `HTTP_PROXY=`;
- `README.md`, `docs/plans/dev-debug-run-script.md`, `docs/tasks/dev-debug-run-script.md` — упоминания `HTTP_PROXY` как опции бота заменить на `TG_PROXY_URL`.

## Чек-лист для разработчика
1. В compose сервис `bot`: `TG_PROXY_URL=${TG_PROXY_URL}`; в environment **нет** `HTTP_PROXY`, `HTTPS_PROXY`, `NO_PROXY`.
2. `options.go` читает `TG_PROXY_URL`, поле переименовано в `TelegramProxyUrl`.
3. `bot/bot.go`: `create()` по-прежнему строит прокси-транспорт для tgbotapi при непустом `TelegramProxyUrl` (проверить сборку `go build ./...`).
4. `bot/analytics/client.go`: `Transport: &http.Transport{Proxy: nil}`.
5. Legacy `bot/backend`: `http.Get`/`http.Post` заменены на клиент с `Proxy: nil`.
6. `docker compose build bot && docker compose up -d bot` → `/login` возвращает ссылку на авторизацию.
7. Telegram-сообщения бота доставляются (трафик к API идёт через Squid) — проверка: при `TG_PROXY_URL` с невалидным адресом бот не стартует/не отвечает, при валидном — работает.
8. `TG_PROXY_URL` пустой в `.env` → работа без прокси, поведение не меняется.
9. Все остальные команды (`/register`, `/confirm`, `/report`, `/add_receipt`) работают как раньше (gRPC-пути не изменены).