# Задача: Retry-логика подключения bot к backend/gRPC при старте

## Бизнес-задача

При развёртывании системы ReceiptCollector сервисы запускаются одновременно (через docker-compose), но bot инициализируется быстрее, чем backend. В текущей реализации bot сразу падает с фатальной ошибкой при попытке подключиться к gRPC-сервисам backend, если они ещё не готовы. Это приводит к нестабильности при деплое и необходимости ручного перезапуска контейнера bot.

### Ценность для заказчика

- **Надёжность развёртывания**: исключение ручного вмешательства при старте системы — сервисы могут запускаться в любом порядке.
- **Отказоустойчивость**: временные проблемы с сетью или перезапуск backend не приводят к падению bot.
- **Упрощение эксплуатации**: не нужно настраивать `depends_on` с условиями готовности (healthcheck) — достаточно стандартного docker-compose.

## Варианты реализации (без глубоких технических деталей)

1. **Retry при инициализации gRPC-клиентов**: при создании подключений к `BACKEND_GRPC_ADDR` и `REPORTS_GRPC_ADDR` bot должен повторять попытки с экспоненциальной задержкой (exponential backoff) вместо немедленного `os.Exit` / `log.Fatal`.
2. **Health-проверка перед запуском**: bot перед стартом основного цикла ожидает, пока все gRPC-сервисы станут доступны, с таймаутом.
3. **Graceful degradation**: если сервис недоступен, bot запускается, но соответствующий функционал помечается как недоступный, с фоновым реконнектом.

Предпочтительный вариант: **комбинация 1 + 3** — retry при старте с exponential backoff, а также механизм фонового восстановления соединения (reconnect) при потере связи во время работы.

## Файлы, которые могут быть затронуты

- `bot/main.go` — инициализация gRPC-клиентов
- `bot/backend/grpc.go` — клиент для backend
- `bot/backend/report/client.go` — клиент для reports
- Возможно, новые файлы для утилит retry/reconnect

## Критерии успеха

1. При старте bot с недоступным backend bot **не падает**, а ожидает подключения в течение разумного таймаута (например, 2-5 минут).
2. Если backend становится доступен в течение таймаута — bot успешно стартует.
3. Если таймаут истёк — bot логирует ошибку и завершается (не зависает бесконечно).
4. При временной потере связи во время работы bot не падает, а пытается переподключиться.
5. Все существующие тесты проходят.

## Архитектурное решение

### Общий подход

Выбран вариант **1 + 3**: retry при старте с exponential backoff + graceful degradation с фоновым reconnect.
Новые файлы не создаются — изменения вносятся только в существующие файлы (минимальные изменения).

### Компонентная схема

```
main.go                      backend/grpc.go              backend/report/client.go
┌─────────────────┐          ┌─────────────────────┐      ┌──────────────────────────┐
│  NewGrpcClient() │          │  GrpcClient          │      │  Client                  │
│  report.New()    │          │  ┌─────────────────┐ │      │  ┌────────────────────┐ │
│  WaitForReady()──┼──────────┼─>│ WaitForReady()   │ │      │  │ subscribe()        │ │
│  user.New()      │          │  │  ┌─────────────┐ │ │      │  │  ┌──────────────┐ │ │
│  start()         │          │  │  │ retry loop   │ │ │      │  │  │ GetReports() │ │ │
└─────────────────┘          │  │  │ exponential  │ │ │      │  │  │ Recv() loop  │ │ │
                             │  │  │ backoff      │ │ │      │  │  └──────────────┘ │ │
                             │  │  └─────────────┘ │ │      │  └────────────────────┘ │
                             │  └─────────────────┘ │      │  reconnect on error───┐ │
                             │  GetUsers() — health  │      └───────────────────────│──┘
                             │  check (спишет users) │                              │
                             └─────────────────────┘        ◄── retry every 5s ─────┘
```

### Детальные изменения

#### 1. `bot/backend/grpc.go` — новый метод `WaitForReady`

**Проблема**: `NewGrpcClient()` выполняет `grpc.Dial()` без `grpc.WithBlock()`, то есть соединение lazy. Первый реальный RPC (в `user.New()`) падает, если backend недоступен.

**Решение**: Добавлен метод `WaitForReady(ctx context.Context) error`, который выполняет health-check (вызов `GetUsers()`) в цикле с exponential backoff:

| Параметр | Значение | Обоснование |
|----------|----------|-------------|
| Начальная задержка | 500 ms | Быстрый повтор при кратковременной недоступности |
| Макс. задержка | 30 с | Ограничение по экспоненциальному росту |
| Таймаут попытки | 5 с | Предотвращение зависания на одной попытке |
| Общий таймаут | 5 мин (в `main.go`) | Задан бизнес-требованием |

Формула задержки: `delay = min(delay * 2, 30s)`, старт с 500ms.

Поведение:
- При успехе — возвращает `nil`, управление передаётся дальше
- При отмене контекста (таймаут / сигнал) — возвращает ошибку
- Каждая попытка логируется

**Почему именно `GetUsers`**: Это самый лёгкий RPC-вызов в backend, не требующий специфичных параметров. Он служит естественным health-check'ом, так как доказывает, что gRPC-сервер готов принимать запросы.

#### 2. `bot/main.go` — вызов `WaitForReady` перед `user.New`

**Было**:
```go
grpcClient := backend.NewGrpcClient(...)
reportsClient := report.New(...)
provider, err := user.New(grpcClient)  // падение при недоступном backend
if err != nil { log.Fatal(err) }
```

**Стало**:
```go
grpcClient := backend.NewGrpcClient(...)
reportsClient := report.New(...)

ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
defer cancel()
if err := grpcClient.WaitForReady(ctx); err != nil {
    log.Printf("Backend gRPC (%s) did not become ready: %v", backendGrpcAddress, err)
    os.Exit(1)
}

provider, err := user.New(grpcClient)  // backend гарантированно доступен
if err != nil { log.Fatal(err) }
```

**Важно**: `reportsClient` создаётся ДО вызова `WaitForReady`. Его `subscribeOnReports()` стартует в фоновой горутине и будет ретраить подключение самостоятельно (см. п.3). Таким образом, `WaitForReady` блокирует только критичный путь (основной gRPC-клиент).

#### 3. `bot/backend/report/client.go` — graceful degradation для streaming

**Проблема**: `subscribeOnReports()` использует `log.Fatalf` при ошибке `GetReports()` или `Recv()`, что убивает весь процесс bot.

**Решение**: Замена `log.Fatalf` на возврат ошибки + внешний reconnect-цикл:

```go
func (c *Client) subscribeOnReports() {
    for {
        err := c.subscribe()
        if err != nil {
            log.Printf("Report subscription lost: %v. Reconnecting in 5s...", err)
            time.Sleep(5 * time.Second)
        }
    }
}

func (c *Client) subscribe() error {
    stream, err := report.GetReports(ctx, &inside.NoParams{})
    if err != nil {
        return fmt.Errorf("GetReports() failed: %w", err)
    }
    for {
        r, err := stream.Recv()
        if err != nil {
            return fmt.Errorf("Recv() failed: %w", err)
        }
        c.Notifications <- r
    }
}
```

Поведение:
- При старте: если backend ещё недоступен, горутина будет ретраить каждые 5 секунд
- Во время работы: если стрим оборвался, горутина переподключается
- Ошибки логируются, но процесс не завершается

### Trade-offs

| Подход | Плюсы | Минусы |
|--------|-------|--------|
| **Выбранный** (WaitForReady + reconnect) | Минимум изменений, не блокирует старт отчётов, устойчив к потере связи | Дублирующий вызов `GetUsers` (в `WaitForReady` и в `user.New`) |
| Альтернатива: `grpc.WithBlock()` при Dial | Единая точка ожидания | Меняет поведение всех RPC, нет гибкости по таймаутам для разных сервисов |
| Альтернатива: отдельный health-check пакет | Чистая архитектура | Избыточен для одного health-check вызова |

### Влияние на существующие тесты

Тесты в `commands/register_test.go` и `commands/code_test.go` тестируют только парсинг сообщений и не используют gRPC. Изменения в gRPC-клиенте не влияют на них. Тесты проходят без изменений.

### ADR

Архитектурное решение зафиксировано в файле `docs/adr/bot-grpc-retry.md`.
