# 🐛 Задача 3/6: Исправление Hardcoded Default User Id

## Приоритет: HIGH (Security)

## Цель
Удалить хардкодный user ID и добавить валидацию на nil перед использованием

---

## Описание проблемы

### Локация: backend/receipts/controller.go строк 86-90

**Текущий код:**
```go
func getUserId(ctx context.Context) string {
    userId := ctx.Value(auth.UserId)
    if userId == nil {
        return "5dc1c9427126cc2841ca384d"  // ❌ HARDCODED ID! SECURITY ISSUE!
    }
    return userId.(string)
}
```

**Проблема:**
- HARDCODED user ID всегда используется как fallback
- Нарушение принципа least-privilege  
- При переходе может видеть чужие данные с разными id'ами

---

## План решения

### Шаг 1: Удалить хардкодный userId и добавить logging

**Вариант А (рекомендуется) - полностью убрать fallback:**
```go
func getUserId(ctx context.Context) string {
    userId := ctx.Value(auth.UserId)
    if userId == nil {
        log.Println("WARNING: No authenticated user in context")
        return ""  // Возвращаем пустой вместо харкода!
    }
    return userId.(string)
}
```

### Шаг 2: Добавить проверки в GetReceiptDetailsHandler

**Текод:**
```go
func (controller Controller) GetReceiptDetailsHandler(writer http.ResponseWriter, request *http.Request) {
    ctx := request.Context()
    defer dispose.Dispose(request.Body.Close, "error while request body close")
    
    id := getReceiptId(writer, request)
    userId := getUserId(ctx)  // ✅ Сейчас вернёт "" если нет auth
    
    if userId == "" {  // ✅ ДОБАВИТЬ проверку на пуста!
        writeErrorStatus(writer, http.StatusUnauthorized)
        return
    }
    
    receipt, err := controller.getReceiptById(ctx, userId, id)
    if err != nil {
        onError(writer, err)
        return
    }
    writeResponse(receipt, writer)
}
```

### Шаг 3: Обновить AddReceiptForTelegramUserHandler строк 176-183

**Текод:**
```go
func (controller Controller) AddReceiptForTelegramUserHandler(writer http.ResponseWriter, request *http.Request) {
    ctx := request.Context()
    receiptRequest := addReceiptRequest{}
    
    err := getFromBody(request, &receiptRequest)  // ❗️ДОПИСывать body!
    if err != nil {
        onError(writer, err)
        return
    }
    
    if receiptRequest.UserId == "" {
        log.Println("No user specified for telegram request")
        return
    }
    
    err = processReceiptQueryString(ctx, &controller.repository, receiptRequest.ReceiptString, receiptRequest.UserId)
    if err != nil {
        onError(writer, err)
        return
    }
}
```

---

## Тестирование

### 1. Проверка удаления харкода:
```bash
# Запрос без аутентификации к protected route:
curl -k http://localhost:8888/api/receipt/<someid> 2>&1 | head -5

# Ожидаемый результат: 401 Unauthorized вместо HARDCODED данных
```

### 2. Проверка auth flow с Telegram bot:
```bash
docker exec receipt-bot start

# В telegram: /start - проверить что видны только свои данные
```

---

## Критерии успеха
- [ ] Хардкодный ID удалён из кода
- [ ] Нет Unauthorized доступа к данным других пользователей  
- [ ] Telegram Bot корректно передаёт userId в контексте
