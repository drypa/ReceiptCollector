# 14. Замена gorilla/mux на стандартный роутер net/http

## Статус
Предложено (ожидает реализации и подтверждения открытых вопросов)

## Контекст
Backend-сервис (`backend/`) использует стороннюю библиотеку `github.com/gorilla/mux v1.8.0`
для HTTP-роутинга. Библиотека долго не имела официального сопровождения, а начиная с
Go 1.22 стандартная библиотека покрывает её ключевую функциональность (переменные пути
`{name}` и привязка HTTP-методов). В `backend/go.mod` указан `go 1.23` — расширенный
`http.ServeMux` доступен без изменений тулчейна.

Задача: docs/tasks/replace-gorilla-mux.md. Цель — убрать зависимость, сохранив поведение
API для клиентов (фронтенд, Telegram-бот) без изменений.

Текущее использование gorilla/mux (проверено grep'ом по репозиторию, вне backend не встречается):

| Место | Использование |
|---|---|
| `backend/main.go:144-191` | `mux.NewRouter()`, регистрация всех маршрутов, `.Methods(...)`, ограничения `{id:[a-zA-Z0-9]+}` |
| `backend/receipts/controller.go:169` | `mux.Vars(request)` через локальный хелпер `getFromQuery` |
| `backend/markets/controller.go:79` | `mux.Vars(request)` напрямую (`vars["id"]`) |
| `backend/users/controller.go:109` | `mux.Vars(request)` через локальный хелпер `getFromQuery(paramName, ...)` |

### Обнаруженный квирк текущего кода (main.go)

```go
router := mux.NewRouter()
// ... регистрация маршрутов ...
http.Handle("/", basicAuth.RequireBasicAuth(router))   // ← DefaultServeMux (строка 162)
http.Handle(registrationRoute, router)                  // ← DefaultServeMux
http.Handle("/internal/", router)                       // ← DefaultServeMux
// ...
s := &http.Server{Addr: address, Handler: router}       // ← сервер использует «сырой» router!
```

Все вызовы `http.Handle(...)` пишут в `DefaultServeMux`, но сервер создаётся с
`Handler: router` — обёртка `RequireBasicAuth` **фактически не применяется**: BasicAuth
сейчас не работает ни на одном маршруте. Следствие видно в контроллерах:
`getUserId(ctx)` (receipts/controller.go:155) при отсутствии `auth.UserId` в контексте
подставляет захардкоженный fallback `"5dc1c9427126cc2841ca384d"`.

При миграции это нужно либо сохранить (поведение «как сейчас»), либо починить
(поведение «как задумано» и как требует критерий приёмки №2) — см. «Открытые вопросы».

## Решение

Перейти на `http.ServeMux` (Go 1.22+ паттерны вида `"METHOD /path/{id}"`) без новых
зависимостей. Зависимость `github.com/goji/httpauth` (используется в `backend/auth/auth.go`)
не трогаем — её замена вне scope задачи.

### 1. Таблица маршрутов (старое → новое)

Прецедентность ServeMux: литеральные сегменты выигрывают у wildcard — конфликтов между
`/api/receipt/from-bar-code`, `/api/receipt/batch` и `/api/receipt/{id}` нет.
Разные методы на одном пути (`GET`+`DELETE /api/receipt/{id}`) выражаются отдельными
паттернами с разными обработчиками — так же, как сегодня.

| Текущая регистрация (gorilla) | Новый паттерн ServeMux |
|---|---|
| `POST /api/user/register` | `"POST /api/user/register"` |
| `/api/market` (без ограничения метода) | `"/api/market"` |
| `PUT,GET,DELETE /api/market/{id:[a-zA-Z0-9]+}` | `"GET /api/market/{id}"`, `"PUT /api/market/{id}"`, `"DELETE /api/market/{id}"` |
| `GET /api/receipt` | `"GET /api/receipt"` |
| `GET /api/receipt/{id:[a-zA-Z0-9]+}` | `"GET /api/receipt/{id}"` |
| `DELETE /api/receipt/{id:[a-zA-Z0-9]+}` | `"DELETE /api/receipt/{id}"` |
| `POST /api/receipt/from-bar-code` | `"POST /api/receipt/from-bar-code"` |
| `POST /api/receipt/batch` | `"POST /api/receipt/batch"` |
| `POST /api/device` | `"POST /api/device"` |
| `GET /api/waste` | `"GET /api/waste"` |
| `POST /api/login` | `"POST /api/login"` |
| `POST /internal/account` | `"POST /internal/account"` |
| `POST /internal/receipt` | `"POST /internal/receipt"` |

### 2. Ограничение `{id:[a-zA-Z0-9]+}` → валидация при извлечении id

ServeMux не поддерживает regex в паттернах: `{id}` матчит один сегмент пути с любыми
символами (кроме `/`), включая точки. Сегодня `abc.def` в `/api/receipt/abc.def` не
матчится роутером → 404; после миграции без доп. мер id попал бы в обработчик.

Выбранное решение: **валидация в общем хелпере извлечения id** (а не middleware):
- точка отказа одна, легко покрыть тестами;
- 404 возвращается ровно там, где раньше его возвращал роутер;
- middleware усложнил бы цепочку и размазал логику статусов.

Новый маленький пакет `backend/route/route.go`:

```go
package route

var idPattern = regexp.MustCompile(`^[a-zA-Z0-9]+$`)

// PathID извлекает параметр пути и проверяет ограничение,
// ранее задававшееся паттерном {id:[a-zA-Z0-9]+}.
func PathID(request *http.Request, name string) (string, bool) {
	id := request.PathValue(name)
	if !idPattern.MatchString(id) {
		return "", false
	}
	return id, true
}
```

Контракт для обработчиков: `!ok` → `http.NotFound` (404) — идентично текущему поведению,
когда маршрут gorilla просто не матчился.

### 3. Замена mux.Vars → request.PathValue

Три места использования имеют одинаковую семантику («достать id из пути»):

- `receipts/controller.go`: тело `getFromQuery` меняется на `request.PathValue(paramName)`
  + валидация через `route.PathID`; `getReceiptId` при невалидном id отвечает 404.
- `markets/controller.go:79`: `vars["id"]` → `route.PathID(request, "id")`.
- `users/controller.go`: тело `getFromQuery` аналогично receipts.

Сигнатуры публичных обработчиков не меняются (они уже принимают `*http.Request`),
изменяются только тела локальных хелперов — минимально инвазивно.

### 4. Структура роутинга и BasicAuth

Два дерева mux + обёртка auth (goji/httpauth остаётся):

```go
func startServer(...) *http.Server {
	// ...
	authedAPI := http.NewServeMux()
	authedAPI.HandleFunc("GET /api/receipt", receiptsController.GetReceiptsHandler)
	// ... остальные /api/* маршруты из таблицы ...

	open := http.NewServeMux()
	open.Handle("/api/", basicAuth.RequireBasicAuth(authedAPI))      // BasicAuth на всём /api/*
	open.HandleFunc("POST /api/login", usersController.LoginHandler) // литералы специфичнее "/api/"
	open.HandleFunc("POST /api/user/register", usersController.UserRegistrationHandler)
	open.HandleFunc("POST /internal/account", usersController.GetUserByTelegramIdHandler)
	open.HandleFunc("POST /internal/receipt", receiptsController.AddReceiptForTelegramUserHandler)

	s := &http.Server{Addr: ":8888", Handler: open}
	// ...
}
```

- Прецедентность гарантирует, что `/api/login` и `/api/user/register` обходят BasicAuth
  (иначе была бы невозможна регистрация/логин).
- `/internal/*` монтируются мимо обёртки — без BasicAuth, как требует критерий №2.
  Telegram-бот использует gRPC и `/internal/*`, поэтому его это не затрагивает.
- Мёртвые вызовы `http.Handle(...)` на `DefaultServeMux` удаляются; сервер получает
  единый корневой handler.

### 5. Совместимость кодов ответов

| Ситуация | gorilla/mux сейчас | ServeMux после миграции |
|---|---|---|
| Неизвестный путь | 404 | 404 ✓ |
| Путь существует, метод не зарегистрирован | 405 | 405 + заголовок `Allow` ✓ |
| Невалидные символы в `{id}` (напр. `a.b`) | 404 (маршрут не матчился) | 404 (валидация в `route.PathID`) ✓ |

Все три случая фиксируются таблицей httptest-тестов (см. план миграции), чтобы регрессия
была поймана в CI.

## План миграции (шаги = атомарные коммиты)

1. **Роутинг в main.go**: `mux.NewRouter()` → два `http.NewServeMux()`; регистрация по
   таблице из п.1; структура BasicAuth из п.4; удаление импорта mux и мёртвых
   `http.Handle`. Проверка: `go build ./...`.
2. **Извлечение id**: пакет `backend/route`; замена тел хелперов в трёх контроллерах
   на `PathValue` + валидация; невалидный id → 404.
3. **Удаление зависимости**: `go mod tidy` — `github.com/gorilla/mux` исчезает из
   `go.mod`/`go.sum`; grep по `*.go` не находит импортов.
4. **Тесты** (`httptest`, без MongoDB — стабы репозиториев): табличный тест
   «маршрут × метод × ожидаемый статус» по всем строкам таблицы п.1; негативные кейсы:
   неверный метод → 405, неизвестный путь → 404, `id=a.b` → 404, `/internal/*` доступен
   без Authorization, `/api/*` без Authorization → 401, с валидными кредами → проходит.
   Проверка: `cd backend && go test ./...`.
5. **Финальная верификация**: сборка Docker-образа backend (`./build.sh` или
   `docker build backend/`); ручной смоук фронтенда и бота.

## Последствия

- **Положительные**: минус одна внешняя зависимость (риск supply-chain, CodeQL);
  роутинг развивается вместе со стандартной библиотекой; явная и читаемая таблица
  маршрутов; исправлена мертвая конфигурация DefaultServeMux в main.go.
- **Отрицательные/риски**:
  - если решим включить реальный BasicAuth (см. открытые вопросы), клиенты, не
    отправлявшие credentials, начнут получать 401 — наблюдаемое изменение;
  - различия в пограничных случаях матчинга (trailing slash, URL-encoding сегментов)
    покрываются только тестами — таблицу негативных кейсов расширять по мере находок;
  - регистрация «метод × путь» многословнее, чем `.Methods(...)` в gorilla.
- **Нейтральные**: goji/httpauth остаётся; fallback-userId в контроллерах не трогаем
  (out of scope).

## Альтернативы

1. **Оставить gorilla/mux** — сопровождение формально восстановлено сообществом, но
   зависимость дублирует возможности stdlib; против цели задачи.
2. **chi / httprouter** — замена одной внешней зависимости другой; против цели задачи.
3. **Валидация id через middleware** вместо хелпера — размазывает логику 404 по цепочке,
   сложнее локализовать тесты; отклонено.
4. **Сохранить фактическое текущее поведение (BasicAuth выключен)** — противоречит
   критерию приёмки №2; требует решения владельца продукта (см. ниже).

## Открытые вопросы (решает владелец продукта до реализации)

1. **Включать ли реальный BasicAuth на `/api/*`?** Сегодня он фактически отключён
   квирком main.go. Критерий №2 требует его включить, но это изменит поведение для
   клиентов без credentials. Рекомендация: включить (это заявленное требование),
   предварительно убедившись, что фронтенд отправляет Authorization.
2. Убирать ли fallback `getUserId` с захардкоженным id? Предлагается оставить
   (изменение бизнес-логики — out of scope).

## Затронутые файлы

- `backend/main.go` — новая схема роутинга, удаление mux и мёртвых `http.Handle`
- `backend/receipts/controller.go` — `getFromQuery`/`getReceiptId` на `PathValue`
- `backend/markets/controller.go` — извлечение id на `route.PathID`
- `backend/users/controller.go` — `getFromQuery` на `PathValue`
- `backend/route/route.go` — новый пакет валидации id
- `backend/route/route_test.go`, тесты роутинга — новые
- `backend/go.mod`, `backend/go.sum` — удаление `github.com/gorilla/mux`
