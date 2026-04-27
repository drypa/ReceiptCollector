# 🐛 Задача 1/6: Исправление утечки контекста

## 📊 Приоритет: 🔴 CRITICAL
**Влияние**: Memory leak, зависание при перезапуске контейнера

---

## 🎯 Цель
Исправить утечку контекста в `backend/main.go`, где воркеры запускаются на общем контексте из main() без правильных отмен.

---

## 🔍 Описание проблемы

### Локация: `backend/main.go` строки 46-79 и 98-112

**Текущий код:**
```go
ctx, cancelFunc := context.WithCancel(context.Background())
client, err := getMongoClient()
if err != nil {
    check(err)
}
deffer dispose.Dispose(func() error {
    return client.Disconnect(context.Background())
}, "error while mongo disconnect")

// ВОРКЕРЫ - запускаются на ctx из main()!
go worker.GetReceiptStart(ctx, settings)
// worker.UpdateRawReceiptStart(ctx, settings)  // КОММЕНТАРИЙ В КОДЕ
worker.GetElectronicReceiptStart(ctx)

creds, err := credentials.NewServerTLSFromFile(...)
if err != nil {
    log.Fatalf("failed to load TLS keys: %v", err)
}

linkClient := link.NewClient(openUrl)
var accountProcessor internal.AccountProcessor = users.NewProcessor(&userRepository, nalogruClient, deviceService, linkClient, clientSecret)
r := render.New(templatePath)

// gRPC listeners
go internal.Serve(":15000", creds, &accountProcessor, &receiptProcessor)
go reports.Serve(":15001", creds, &userRepository, &receiptReportRepository)

server := startServer(...)

sigChan := make(chan os.Signal)
signal.Notify(sigChan, os.Kill)
signal.Notify(sigChan, os.Interrupt)
sig := <-sigChan // <-- СЮДА ПРИХОДИТ SHUTDOWN

log.Printf("Service is shutting down... %s\n", sig)
cancelFunc() // ❌ ОТМЕНА ПОСЛЕ ШУТДАУНА HTTP!
ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
err = server.Shutdown(ctx)
```

**Проблема:**
1. Все воркеры (`worker.GetReceiptStart`, `worker.GetElectronicReceiptStart`) работают на общем контексте из main()
2. После shutdown HTTP сервера вызывается `cancelFunc()` с отменой родительского контекста
3. Но goroutines внутри воркеров продолжают работу, не получая корректной отмены
4. Это приводит к утечке памяти и запросам на фон после остановки сервиса

---

## ✅ План решения

### Шаг 1: Создать отдельные контексты для каждого воркера
[Посмотреть полный план в backend/main.go](main-go-context-fix.md)

**Что изменить:**

```go
// ПОСЛЕ строки getMongoClient():

// ВОРКЕР 1 - GetReceiptStart получает свой context с timeout=1мин
receiptCtx, receiptCancel := context.WithTimeout(ctx, 60*time.Second)
go worker.GetReceiptStart(receiptCtx, settings)
deffer receiptCancel() // ⏳ ДОБАВИТЬ для cleanup

// ВОРКЕР 2 - ElectronicReceipt запускается раз в сутки (длинный интервал)
eRecCtx, eRecCancel := context.WithTimeout(ctx, 60*time.Minute)
worker.GetElectronicReceiptStart(eRecCtx) // ⚠️ НЕ go, один раз в сутках!
deffer eRecCancel() // ⏳ ДОБАВИТЬ для cleanup

// ВОРКЕР 3 - Device maintenance (если нужно возобновить)
deviceCtx, deviceCancel := context.WithTimeout(ctx, 5*time.Minute)
go worker.DeviceMaintenance(deviceCtx, settings) // ❗️Добавить функцию если нужно
deffer deviceCancel() // ⏳ ДОБАВИТЬ для cleanup
```


### Шаг 2: Убедиться что все cancel вызовы работают в shutdown
```go
sig := <-sigChan

log.Printf("Service shutting down... %s", sig)

// ⏺️ ОТМЕНА ВСЕХ КОНТЕКСТОВ В ПРАВИЛЬНОМ ПОРЯДКУ (длинный → короткий):
deviceCancel() // Сначала короткоживущий (5 мин)
receiptCancel() // Затем средний интервал (1 мин)  
eRecCancel()    // Затем долгий (60 мин)
reportsCancel() // Затем gRPC reports
cancelFunc()   // В конце родительский контекст

ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
err = server.Shutdown(ctx)
```

---

## 📝 Тестирование

### 1. Проверка корректного завершения воркеров:
```bash
cd backend && go build -o receipt_collector .
./up.sh

# Мониторинг логов на shutdown при SIGTERM
docker exec receipt-collector tail -f /var/log/collector.log

# Ищем сообщения в логах: "finished", "cleaning up"
sleep 10
```

### 2. Проверка очистки при перезапуске:
```bash
# Убедиться что контейнер очищается после shutdown (не зависает)
docker logs receipt-collector --tail 30 | grep -i "shutdown\|cancel\|finished"

# Проверка отсутствия утечек:
docker stats receipt-collector
```

### 3. Stress-тест многократного перезапуска:
```bash
for i in {1..5}; do
    echo "Restart $i of 5..."
    ./down.sh
    sleep 2
    ./up.sh
done

docker logs receipt-collector --tail 50 | grep -i error
echo "=== Мультитест завершен. Ошибок: $? ==="
```


---

## 🎯 Критерии успеха

- [ ] **Контексты создаются с правильными timeout** для каждого воркера  
- [ ] **Все cancel() вызываются в shutdown** перед server.Shutdown()  
- [ ] После SIGTERM воркеры корректно завершают работу за < 5 сек  
- [ ] Нет утечек памяти при запуске/остановкe цикла: restart → stop → start (10+) раз  
- [ ] В логах после shutdown нет сообщений о продолжении работы воркеров  

---

## 🔗 Связанные файлы
- `main.go` - основной файл для изменений
- [`TASK-02-race-device.md`](/home/drypa/projects/ReceiptCollector/.opencode/plans/TASK-02-race-condition-device.md) - следующая задача  
- [AGENTS.md](file:///home/drypa/projects/ReceiptCollector/AGENTS.md) - архитектура проекта

