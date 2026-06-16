# Задачи: Retry-логика подключения bot к backend через gRPC

## Общий план

| № | Задача | P | Время | Зависимости |
|---|--------|---|-------|------------|
| 1 | Добавить метод `WaitForReady` в `GrpcClient` | P0 | 2ч | — |
| 2 | Graceful degradation в report-клиенте (`subscribeOnReports`) | P0 | 1.5ч | — |
| 3 | Вызов `WaitForReady` в `main.go` перед `user.New()` | P1 | 1ч | Задача 1 |
| 4 | Проверка: сборка, тесты, code review | P1 | 0.5ч | Задачи 1, 2, 3 |

**Итого**: ~5 часов разработки.

**Порядок выполнения**:
1. Задача 1 и Задача 2 — параллельно (независимые файлы)
2. Задача 3 — после Задачи 1
3. Задача 4 — после выполнения всех задач

---

## Задача 1: Добавить метод `WaitForReady` в `GrpcClient`

**P0** | **~2 часа**

### Описание
Добавить на `GrpcClient` метод `WaitForReady(ctx context.Context) error`, который выполняет health-check (вызов `GetUsers()`) в цикле с exponential backoff. Метод блокируется до тех пор, пока backend gRPC не станет доступен, или пока не будет отменён контекст (таймаут из `main.go`).

### Файлы для изменения
- `bot/backend/grpc.go`

### Технические детали реализации

**Импорты** (добавить в существующий блок import):
```go
"fmt"
"math"
"time"
```

**Константы** (добавить после последнего метода `VerifyPhone`, перед закрывающей `}` пакета — или в конец файла):
```go
const (
	waitForReadyInitialDelay    = 500 * time.Millisecond
	waitForReadyMaxDelay        = 30 * time.Second
	waitForReadyAttemptTimeout  = 5 * time.Second
)
```

| Константа | Значение | Назначение |
|-----------|----------|------------|
| `waitForReadyInitialDelay` | 500ms | Начальная задержка перед первой retry-попыткой |
| `waitForReadyMaxDelay` | 30s | Максимальная задержка (cap для exponential backoff) |
| `waitForReadyAttemptTimeout` | 5s | Таймаут на одну health-check попытку |

**Метод** (сигнатура и реализация):
```go
// WaitForReady blocks until the gRPC connection is usable or the context is cancelled.
// It performs a health check (GetUsers) with exponential backoff.
// Maximum backoff delay is capped at 30 seconds; overall timeout is controlled by ctx.
func (c *GrpcClient) WaitForReady(ctx context.Context) error {
	delay := waitForReadyInitialDelay

	for {
		checkCtx, cancel := context.WithTimeout(ctx, waitForReadyAttemptTimeout)
		_, err := c.GetUsers(checkCtx)
		cancel()

		if err == nil {
			log.Println("Backend gRPC connection is ready")
			return nil
		}

		log.Printf("Backend gRPC not ready: %v. Next attempt in %v", err, delay)

		select {
		case <-ctx.Done():
			return fmt.Errorf("waiting for backend gRPC cancelled: %w", ctx.Err())
		case <-time.After(delay):
			delay = time.Duration(math.Min(float64(delay*2), float64(waitForReadyMaxDelay)))
		}
	}
}
```

**Алгоритм работы**:
1. `delay` инициализируется 500ms
2. На каждой итерации:
   - Создаётся дочерний контекст с таймаутом 5s (`checkCtx`)
   - Выполняется `GetUsers(checkCtx)` — самый лёгкий RPC backend, служит health-check
   - Контекст попытки отменяется
   - При успехе — возврат `nil`
   - При ошибке — логирование, затем ожидание с задержкой
3. Exponential backoff: `delay = min(delay * 2, 30s)`
4. Если родительский контекст отменён — возврат ошибки с `ctx.Err()`

### Критерии приёмки
- Код компилируется: `go build ./...` в `bot/`
- Метод корректно реализует exponential backoff (500ms → 1s → 2s → ... → 30s cap)
- Каждая попытка имеет собственный таймаут 5s (не блокируется бесконечно)
- При отмене контекста возвращается ошибка, содержащая `ctx.Err()`
- При успехе возвращается `nil`
- Логи пишутся на каждую попытку

### Тестирование
- **Unit-тесты не требуются** — логика (exponential backoff, таймауты) хорошо видна в коде; полное тестирование требует запущенного gRPC-сервера.
- Достаточно code review и ручной проверки:
  ```bash
  cd bot && go vet ./backend/
  go build ./...
  ```

---

## Задача 2: Graceful degradation в report-клиенте (`subscribeOnReports`)

**P0** | **~1.5 часа**

### Описание
Переделать `subscribeOnReports()` в `bot/backend/report/client.go`: заменить фатальные ошибки (`log.Fatalf`) на возврат ошибки и бесконечный reconnect-цикл с задержкой 5 секунд. Это предотвращает падение всего bot при временной недоступности backend во время работы.

### Файлы для изменения
- `bot/backend/report/client.go`

### Технические детали реализации

**Импорты** (добавить в существующий блок import):
```go
"fmt"
"time"
```

**Константа** (добавить после структуры `Client`, перед методом `subscribeOnReports`):
```go
const reportsReconnectDelay = 5 * time.Second
```

**Рефакторинг методов**:

Текущая структура (до изменений):
```go
func (c *Client) subscribeOnReports() {
	ctx := context.Background()
	report := *(c.report)
	stream, err := report.GetReports(ctx, &inside.NoParams{}, grpc.EmptyCallOption{})
	if err != nil {
		log.Fatalf(...)
	}
	for {
		report, err := stream.Recv()
		if err != nil {
			log.Fatalf(...)
		}
		log.Printf("Send report %v", *report)
		c.Notifications <- report
	}
}
```

Новая структура (после изменений):

1. **`subscribeOnReports()`** — внешний бесконечный цикл переподключения:
```go
func (c *Client) subscribeOnReports() {
	for {
		err := c.subscribe()
		if err != nil {
			log.Printf("Report subscription lost: %v. Reconnecting in %v...", err, reportsReconnectDelay)
			time.Sleep(reportsReconnectDelay)
		}
	}
}
```

2. **`subscribe() error`** — новый метод, возвращает ошибку вместо `log.Fatalf`:
```go
func (c *Client) subscribe() error {
	ctx := context.Background()
	report := *(c.report)
	stream, err := report.GetReports(ctx, &inside.NoParams{})
	if err != nil {
		return fmt.Errorf("GetReports() failed: %w", err)
	}
	for {
		r, err := stream.Recv()
		if err != nil {
			return fmt.Errorf("Recv() failed: %w", err)
		}
		log.Printf("Send report %v", *r)
		c.Notifications <- r
	}
}
```

**Что меняется**:
- `subscribeOnReports` больше не содержит логику подписки — только reconnect-цикл
- Вся логика gRPC-стрима вынесена в `subscribe() error`
- `log.Fatalf` → `return fmt.Errorf(...)` (graceful, не убивает процесс)
- Удалён `grpc.EmptyCallOption{}` из вызова `GetReports` (необязательный устаревший параметр)
- Переименована переменная `report` → `r` внутри цикла `Recv()` (устранение конфликта с именем пакета)

### Критерии приёмки
- Код компилируется: `go build ./...` в `bot/`
- При ошибке `GetReports()` или `Recv()` процесс не падает, а пишет лог и повторяет попытку через 5 секунд
- При успешном подключении стрим работает как раньше: полученные уведомления отправляются в канал `Notifications`
- Все существующие тесты проходят: `go test ./...` в `bot/`

### Тестирование
- **Unit-тесты не требуются** — изменение затрагивает только асинхронный reconnect-цикл; полное тестирование требует запущенного gRPC-сервера.
- Code review:
  - Убедиться, что нет утечки ресурсов (контекст `context.Background()` без отмены — это нормально, так как стрим должен жить вечно)
  - Убедиться, что канал `Notifications` используется корректно (буферизированный или неблокирующий — проверить по месту использования)

---

## Задача 3: Вызов `WaitForReady` в `main.go` перед `user.New()`

**P1** | **~1 час**

### Описание
Добавить вызов `grpcClient.WaitForReady(ctx)` в `bot/main.go` перед `user.New()`. Это гарантирует, что bot не стартует, пока backend gRPC не станет доступен (с таймаутом 5 минут).

### Файлы для изменения
- `bot/main.go`

### Зависимости
- Задача 1 должна быть выполнена (метод `WaitForReady` должен существовать)

### Технические детали реализации

**Импорты** (добавить в существующий блок import):
```go
"context"
"time"
```

**Порядок импортов** (стандартный Go — отсортировать по алфавиту):
```go
import (
	"context"
	"log"
	"os"
	"time"

	"github.com/drypa/ReceiptCollector/bot/analytics"
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/report"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	"github.com/drypa/ReceiptCollector/bot/commands"
	"google.golang.org/grpc/credentials"
)
```

**Изменение в `main()`**:

Было:
```go
grpcClient := backend.NewGrpcClient(backendGrpcAddress, creds)
reportsClient := report.New(reportsGrpcAddress, creds)

provider, err := user.New(grpcClient)
if err != nil {
	log.Fatal(err)
}
```

Стало:
```go
grpcClient := backend.NewGrpcClient(backendGrpcAddress, creds)
reportsClient := report.New(reportsGrpcAddress, creds)

// Wait for backend gRPC to become ready with a 5-minute timeout.
// During this time the bot retries the connection with exponential backoff.
ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
defer cancel()
if err := grpcClient.WaitForReady(ctx); err != nil {
	log.Printf("Backend gRPC (%s) did not become ready within timeout: %v", backendGrpcAddress, err)
	os.Exit(1)
}

provider, err := user.New(grpcClient)
if err != nil {
	log.Fatal(err)
}
```

**Важно**:
- `reportsClient` создаётся **ДО** вызова `WaitForReady`. Его `subscribeOnReports()` стартует в фоновой горутине (вызывается в `report.New()`) и будет ретраить подключение самостоятельно (см. Задача 2).
- `WaitForReady` блокирует только критичный путь — основной gRPC-клиент.

### Критерии приёмки
- Код компилируется: `go build ./...` в `bot/`
- При запуске с недоступным backend bot ожидает до 5 минут (не падает сразу)
- Если backend стал доступен — bot продолжает старт
- Если прошло 5 минут — bot пишет лог и завершается с `os.Exit(1)`
- `reportsClient` создаётся независимо и его reconnect работает в фоне

### Тестирование
- **Unit-тесты не требуются** — интеграционное поведение; зависит от запущенного backend.
- Ручная проверка:
  - Запустить bot без backend → проверить логи: retry-попытки с exponential backoff
  - Запустить backend в течение 5 минут → bot должен успешно стартовать
  - Проверить, что при `os.Exit(1)` код возврата ненулевой

---

## Задача 4: Проверка: сборка, тесты, code review

**P1** | **~0.5 часа**

### Описание
Финальная проверка: убедиться, что код компилируется, существующие тесты проходят, и изменения соответствуют ADR и требованиям.

### Файлы для изменения
- Не требует изменения файлов (только проверка)

### Зависимости
- Задача 1, Задача 2, Задача 3 — выполнены

### Процедура проверки

1. **Сборка**:
   ```bash
   cd bot && go build ./...
   ```
   Ожидаемый результат: код компилируется без ошибок.

2. **Линтер**:
   ```bash
   cd bot && go vet ./...
   ```
   Ожидаемый результат: `vet` не находит проблем.

3. **Тесты**:
   ```bash
   cd bot && go test ./... -v
   ```
   Ожидаемый результат: все тесты проходят (должны быть `PASS` или `ok`).

4. **Code Review (чеклист)**:

   - [ ] `bot/backend/grpc.go`:
     - [ ] Импорты `fmt`, `math`, `time` добавлены
     - [ ] Константы `waitForReadyInitialDelay`, `waitForReadyMaxDelay`, `waitForReadyAttemptTimeout` определены
     - [ ] `WaitForReady(ctx context.Context) error` реализован с exponential backoff
     - [ ] Попытка имеет таймаут 5s (через `context.WithTimeout`)
     - [ ] Задержка удваивается, cap 30s
     - [ ] При `ctx.Done()` возвращается ошибка с `ctx.Err()`
     - [ ] Каждая попытка логируется
     - [ ] Пакет `log` уже импортирован (используется в `NewGrpcClient`)

   - [ ] `bot/backend/report/client.go`:
     - [ ] Импорты `fmt`, `time` добавлены
     - [ ] Константа `reportsReconnectDelay = 5 * time.Second` определена
     - [ ] `subscribeOnReports()` содержит бесконечный reconnect-цикл
     - [ ] Выделен метод `subscribe() error`
     - [ ] `log.Fatalf` заменён на возврат ошибки
     - [ ] `grpc.EmptyCallOption{}` удалён из вызова `GetReports`
     - [ ] Переменная `report` в цикле `Recv()` переименована в `r`

   - [ ] `bot/main.go`:
     - [ ] Импорты `context`, `time` добавлены
     - [ ] `WaitForReady` вызывается после создания `grpcClient` и `reportsClient`
     - [ ] `reportsClient` создаётся до `WaitForReady`
     - [ ] `context.WithTimeout` на 5 минут
     - [ ] При ошибке `WaitForReady`: `log.Printf` + `os.Exit(1)`
     - [ ] После успешного `WaitForReady` — вызов `user.New()` как и было

5. **Проверка на соответствие ADR** (docs/adr/004-bot-grpc-retry.md):
   - [ ] Метод `WaitForReady` с exponential backoff (500ms → ... → 30s cap)
   - [ ] Graceful degradation в report-клиенте (reconnect, не `log.Fatalf`)
   - [ ] `reportsClient` создаётся до `WaitForReady`
   - [ ] Таймаут 5 минут

### Критерии приёмки
- `go build ./...` проходит без ошибок
- `go vet ./...` проходит без ошибок
- `go test ./...` проходит — все тесты зелёные
- Code review не выявил проблем

---

## Приложение: Полный diff ожидаемых изменений

### `bot/backend/grpc.go`

```diff
+import (
+	"fmt"
+	"math"
+	"time"
+)

+const (
+	waitForReadyInitialDelay    = 500 * time.Millisecond
+	waitForReadyMaxDelay        = 30 * time.Second
+	waitForReadyAttemptTimeout  = 5 * time.Second
+)
+
+func (c *GrpcClient) WaitForReady(ctx context.Context) error {
+	delay := waitForReadyInitialDelay
+	for {
+		checkCtx, cancel := context.WithTimeout(ctx, waitForReadyAttemptTimeout)
+		_, err := c.GetUsers(checkCtx)
+		cancel()
+		if err == nil {
+			log.Println("Backend gRPC connection is ready")
+			return nil
+		}
+		log.Printf("Backend gRPC not ready: %v. Next attempt in %v", err, delay)
+		select {
+		case <-ctx.Done():
+			return fmt.Errorf("waiting for backend gRPC cancelled: %w", ctx.Err())
+		case <-time.After(delay):
+			delay = time.Duration(math.Min(float64(delay*2), float64(waitForReadyMaxDelay)))
+		}
+	}
+}
```

### `bot/backend/report/client.go`

```diff
+import (
+	"fmt"
+	"time"
+)

+const reportsReconnectDelay = 5 * time.Second

 func (c *Client) subscribeOnReports() {
+	for {
+		err := c.subscribe()
+		if err != nil {
+			log.Printf("Report subscription lost: %v. Reconnecting in %v...", err, reportsReconnectDelay)
+			time.Sleep(reportsReconnectDelay)
+		}
+	}
+}
+
+func (c *Client) subscribe() error {
 	ctx := context.Background()
 	report := *(c.report)
-	stream, err := report.GetReports(ctx, &inside.NoParams{}, grpc.EmptyCallOption{})
+	stream, err := report.GetReports(ctx, &inside.NoParams{})
 	if err != nil {
-		log.Fatalf("%v.GetReports() failed with %v", c.report, err)
+		return fmt.Errorf("GetReports() failed: %w", err)
 	}
 	for {
-		report, err := stream.Recv()
+		r, err := stream.Recv()
 		if err != nil {
-			log.Fatalf("%v.Recv() failed with %v", stream, err)
+			return fmt.Errorf("Recv() failed: %w", err)
 		}
-		log.Printf("Send report %v", *report)
-		c.Notifications <- report
+		log.Printf("Send report %v", *r)
+		c.Notifications <- r
 	}
 }
```

### `bot/main.go`

```diff
+import (
+	"context"
+	"time"
+)

 func main() {
 	...
 	grpcClient := backend.NewGrpcClient(backendGrpcAddress, creds)
 	reportsClient := report.New(reportsGrpcAddress, creds)
+
+	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
+	defer cancel()
+	if err := grpcClient.WaitForReady(ctx); err != nil {
+		log.Printf("Backend gRPC (%s) did not become ready within timeout: %v", backendGrpcAddress, err)
+		os.Exit(1)
+	}
+
 	provider, err := user.New(grpcClient)
 	...
 }
```
