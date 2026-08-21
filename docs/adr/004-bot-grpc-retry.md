# 4. Retry-логика подключения bot к backend через gRPC

## Статус
Принято

## Контекст
При развёртывании системы ReceiptCollector через docker-compose сервисы запускаются одновременно, но Telegram bot (Go) инициализируется быстрее, чем backend (Go). В текущей реализации bot сразу падает с `log.Fatal` при попытке вызвать `user.New()` (который выполняет `GetUsers` gRPC-запрос) или при старте report-стрима (`GetReports` в фоновой горутине), если backend ещё не готов. Это требует ручного перезапуска контейнера bot.

Дополнительно: при временной потере связи во время работы bot также падает из-за `log.Fatalf` в report-клиенте.

## Решение
Принята комбинация двух подходов:
1. **Retry при старте с exponential backoff** — `WaitForReady` перед первым критичным RPC-вызовом
2. **Graceful degradation** — фоновый reconnect для не-критичного функционала (report streaming)

### WaitForReady (backend/grpc.go)
Новый метод `WaitForReady(ctx context.Context) error` на `GrpcClient`:
- Выполняет `GetUsers()` как health-check в цикле
- Использует exponential backoff: 500ms → 1s → 2s → 4s → ... → 30s (cap)
- Каждая попытка имеет собственный таймаут 5s
- Общий таймаут контролируется через `ctx` (в main — 5 минут)

### Graceful degradation (backend/report/client.go)
- Замена `log.Fatalf` на возврат ошибки
- Бесконечный цикл переподключения с задержкой 5s между попытками
- Ошибки логируются, процесс не завершается

### main.go
- `reportsClient` создаётся до `WaitForReady` — его reconnect работает независимо
- `WaitForReady` вызывается перед `user.New()`, блокируя только критичный путь

## Последствия
- **Положительные**: бот не падает при недоступном backend, авто-восстановление после временных проблем, совместимость со стандартным docker-compose
- **Отрицательные**: дублирующий вызов `GetUsers` (в `WaitForReady` и затем в `user.New`); небольшое увеличение времени старта при здоровом backend (1 health-check запрос)
- **Нейтральные**: новые константы в пакетах `backend` и `report`

## Альтернативы
1. **`grpc.WithBlock()` при Dial** — меняет поведение всех RPC, нет гибкости таймаутов на уровне отдельных сервисов.
2. **Отдельный health-check сервис** — избыточно для одного проверочного RPC-вызова.
3. **Retry только в main.go** (без выделения `WaitForReady`) — смешивает ответственность main-пакета и транспортного уровня.

## Затронутые файлы
- `bot/backend/grpc.go` — добавлен `WaitForReady`, константы, импорты
- `bot/backend/report/client.go` — заменён `subscribeOnReports` на reconnect-цикл
- `bot/main.go` — добавлен вызов `WaitForReady` перед `user.New()`
