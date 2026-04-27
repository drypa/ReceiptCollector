# 🐛 Задача 4/6: Исправление пропущенного чтения HTTP body

## Приоритет: HIGH  
**Влияние**: Бот не может добавлять чек без тела запроса -> падает на 500

---

## Описание проблемы

### Локация: backend/users/link/link.go строк 58-59

**Текущий код:**
```go
func (service *Service) GetUserByTelegramId(ctx context.Context, telegramId int64) (*UserLinkageModel, error) {
    ...
    userLinkage := &UserLinkageModel{UserId: linkedUserId}
    
    ctx, cancel := context.WithTimeout(ctx, 10*time.Second)
    defer cancel()

    // ←←← ПЕРЕХОД К TELEGRAM API
    res, _ := client.Post(shareUrl+urlEncodedIdPath, map[string]string{})  // ❗️NO BODY READ!
    
    body, err := io.ReadAll(res.Body)  // ←←← Чтение тела ПЕРЕД декоиring!
    defer dispose.Dispose(func() error { return res.Body.Close() }, "failed to close HTTP resp")
    
    if err != nil {
        log.Printf("failed to read telegram response: %v", err)  
        return nil, InternalError
    }

    var model UserLinkageModel
    err = json.Unmarshal(body, &model)  // ←←← DECODE ПОСЛЕ BODY READ!
    ...
}
```

**Проблема:**
- `json.NewDecoder(res.Body).Decode(&userLinkage)` пытается декодировать body напрямую  
- При ошибке в Telegram API или невалидном ответе это вызовет panic/ошибку
- Нужно читать body полностью перед декоммирования

---

## План решения

### Шаг 1: Исправить GetUserByTelegramId в backend/users/link/link.go

**Текод:**
```go
func (service *Service) GetUserByTelegramId(ctx context.Context, telegramId int64) (*UserLinkageModel, error) {
    ...
    
    ctx, cancel := context.WithTimeout(ctx, 10*time.Second)
    defer cancel()

    res, err := client.Post(fmt.Sprintf("%s?url=%s", shareUrl, urlEncodedIdPath), map[string]string{})
    if err != nil {
        log.Printf("Failed to call telegram API: %v", err)
        return nil, fmt.Errorf("telegram api error: %w", err)
    }
    
    defer dispose.Dispose(func() error { return res.Body.Close() }, "failed to close HTTP resp")

    // ←←← 1. Читаем тело ПЕРВЫМ!
    body, err := io.ReadAll(res.Body)
    if err != nil {
        log.Printf("Failed to read telegram API response: %v", err)  
        return nil, fmt.Errorf("failed to read response: %w", err)
    }
    
    // ←←← 2. Проверяем status code!
    if res.StatusCode != http.StatusOK {
        log.Printf("Telegram API returned non-OK status %d for telegramId=%d", res.StatusCode, telegramId)
        return nil, fmt.Errorf("invalid response from telegram api: %s", string(body))
    }

    var model UserLinkageModel
    err = json.Unmarshal(body, &model)  // ←←← ✅ Теперь безопасно декорировать!
    
    if err != nil {
        log.Printf("Failed to decode telegram user linkage response: %v. body: %s", err, string(body))
        return nil, InternalError
    }

    return &model, nil
}
```

### Шаг 2: Добавить аналогичное чтение в AddReceiptForTelegramUserHandler (str 176-183 backend/receipts/controller.go)

**Текод:**
```go
func (controller Controller) AddReceiptForTelegramUserHandler(writer http.ResponseWriter, request *http.Request) {
    ctx := request.Context()
    
    // ←←← Читаем тело запроса ПЕРЕД декодированием!
    var receiptRequest addReceiptRequest
    err := getFromBody(request, &receiptRequest)  // ИСПОЛЬЗУЕМ EXISTING функцию!
    if err != nil {
        onError(writer, err)
        return
    }
    
    // Или явно читаем:
    body, err := io.ReadAll(request.Body)
    if err != nil {
        onError(writer, err)
        return
    }
    
    err = json.Unmarshal(body, &receiptRequest)
    if terr != nil {
        log.Printf("Failed to decode telegram user receipt request: %v", terr)
        onError(writer, InternalError)
        return
    }

    // Остальная обработка...  
}
```

---

## Тестирование

### 1. Проверка обработки невалидного ответа от Telegram API:

```bash
# Эмулируем ошибку в телеграме:
export TELEGRAM_BOT_TOKEN=ваш_token
# (или mock)

curl -k -X POST http://localhost:8888/internal/account \  -d "url=https%3F"  # невалидный urlEncodedIdPath

# Ожидаемо: HTTP 500 + лог ошибок в backend logs, а не crash
```

### 2. Проверка добавления чех через telegram:
```bash
docker exec receipt-bot addreceipt \  --qr "some-qr-encoded" \  --telegramId 123456789
      
# Убедимся что невалидный ответ обрабатывается корректно и бот не падает
```

---

## Критерии успеха  
- [ ] Body читается через io.ReadAll перед json.Unmarshal() в GetUserByTelegramId
- [ ] Status code проверяется после POST к Telegram API
- [ ] Навалидные ответы вызывают HTTP 500 с понятными логов (не panic)
- [ ] Бот продолжает работать при ошибках Telegram API

