# 🐛 Задача 2/6: Исправление race condition с device Free

## Приоритет: CRITICAL  
**Влияние**: Device могут быть освобождены с отменённым контекстом, вызывая panic

---

## Цель
Исправить неправильное использование defer worker.devices.Free(ctx, device) в workers/get.go

---

## Описание проблемы

### Локация: backend/workers/get.go строк 52-92

**Текущий код:**
```go
device, err := worker.devices.Rent(ctx)
defer worker.devices.Free(ctx, device)    // ❌ Проблема здесь!
id, err := worker.nalogruClient.GetTicketId(normalizedQr, device)

if err != nil {
    return err  // Deferred Free вызовется - но ctx может быть отменён!
}
```

**Проблема:**
- defer вызывается при любом возвращении из функции даже после cancel контекста
- При отмене main() defer пытается освободить device с уже cancelled ctx -> panic  

---

## План решения

### Шаг 1: Добавить проверкu контекста перед free в defer wrapper

**Исправленный код:**
```go
device, err := worker.devices.Rent(ctx)
if err != nil {
    return err
}

// Добавляем safe_free wrapper:
defer func() {    // ❗️Обернуть в анонимную функцию для проверки ctx
    select {
    case <-ctx.Done():     // ✅ Если ctx уже отменён - пропускаем free
        log.Println("Context cancelled, skipping device Free")
    default:               // ✅ Иначе пытаемся free с проверкой ошибок
        err = worker.devices.Free(ctx, device)
        if err != nil {
            log.Printf("Failed to Free device: %v", err)
        }
    }
}()

// Остальной код
```

### Шаг 2: Удалить комментарий `//go:noinline` из refreshSession строк 116-130  
**Исключите неиспользуемую функцию:** В конце файла закомментируйте:
```go
//func (worker *Worker) refreshSession(ctx context.Context) error {
//err := worker.nalogruClient.RefreshSession()
...
}
```

### Шаг 3: Переместить defer за весь блок try-catch  

**Исправление в workers/get.go:**
```go
func (worker *Worker) getReceipt(ctx context.Context) error {
    receipt, err := worker.repository.GetWithoutTicket(ctx)
    
    // ... обработка без device - OK!

    device, err := worker.devices.Rent(ctx)  // ПОЛУЧАЕМ DEVICE
    if err != nil {
        return err
    }

    id, err := worker.nalogruClient.GetTicketId(normalizedQr, device)
    
    // ... вся обработка с проверками
    
    // DEAR - ПЕРЕМЕСТИТЬ В КОНЕЦ ФУНКЦИИ ПОСЛЕ ВСЕЙ ОБРАБОТКИ!
    defer func() { 
        select {
        case <-ctx.Done():
            log.Println("Context cancelled, skipping device cleanup")
        default:
            _ = worker.devices.Free(ctx, device)  // Проверка errors внутри Free
        }
    }()

    return nil
}
```

---

## Тестирование

### Стресс-тест:
```bash
cd backend && go build -o receipt_collector .

# Запустите с коротким интервалом для генерации ошибок:
export GET_RECEIPT_WORKER_INTERVAL=30s
./up.sh

# Ищите в логах ошибки с Free:
docker logs receipt-collector --tail 100 | grep -i "fail to free\|race condition"

# Проверка graceful shutdown без ошибок:
docker stop receipt-collector && sleep 2
docker logs receipt-collector --tail 50 
echo "=== Shutdown test finished ==="
```

---

## Критерии успеха
- [ ] Defer освобождает device с проверенным контекстом
- [ ] При cancel(context) не происходит panic при освобожени
- [ ] DailyLimitReached обрабатывается без утечки devices  
- [ ] Graceful shutdown завершается за < 3 сек
