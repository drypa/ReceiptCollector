# ADR-017: Обновление CodeQL GitHub Actions с v1 на v3

## Статус

Принято

## Контекст

Workflow CodeQL (`.github/workflows/codeql-analysis.yml`) использует deprecated action-шаги
`github/codeql-action/*@v1` (`init`, `autobuild`, `analyze`). GitHub прекращает поддержку
v1 (Node 16 runtime), в перспективе workflow может перестать работать.

Текущее состояние workflow:

```yaml
- uses: actions/checkout@v2           # ← тоже deprecated (Node 12)
- uses: github/codeql-action/init@v1
- uses: github/codeql-action/autobuild@v1
- run: git checkout HEAD^2            # ← workaround для v1
- uses: github/codeql-action/analyze@v1
```

Workflow запускается на триггерах: push/PR в master, еженедельный cron.
Анализируются языки: `go`, `javascript`.

## Решение

### 1. Замена версий CodeQL actions: `@v1` → `@v3`

| Шаг | Было | Стало |
|-----|------|-------|
| Init | `github/codeql-action/init@v1` | `github/codeql-action/init@v3` |
| Autobuild | `github/codeql-action/autobuild@v1` | `github/codeql-action/autobuild@v3` |
| Analyze | `github/codeql-action/analyze@v1` | `github/codeql-action/analyze@v3` |

**Обоснование:** v3 — текущая стабильная версия, использует Node 20. Интерфейс
(`languages`, `queries` параметры) совместим с v1, дополнительная конфигурация
не требуется. Триггеры и matrix-стратегия остаются без изменений.

### 2. Обновление checkout: `@v2` → `@v4` (рекомендовано)

| Шаг | Было | Стало |
|-----|------|-------|
| Checkout | `actions/checkout@v2` | `actions/checkout@v4` |

**Обоснование:** `actions/checkout@v2` использует Node 12, также deprecated.
v4 использует Node 20. Интерфейс (`fetch-depth`, `with`) полностью совместим.

### 3. Удаление шага `git checkout HEAD^2` (рекомендовано)

Шаг `run: git checkout HEAD^2` с условием `if: pull_request` — workaround
из эпохи v1. CodeQL v3 корректно работает с merge commit, который
`actions/checkout` предоставляет по умолчанию. Шаг удаляется как не нужный.

**Риск удаления:** Минимальный. Если вдруг возникнут проблемы с PR-анализом,
шаг легко вернуть. Но в актуальной документации GitHub этот workaround
больше не упоминается.

### 4. Явные permissions (рекомендовано, опционально)

Добавить в начало job:

```yaml
permissions:
  security-events: write
```

**Обоснование:** Метод наименьших привилегий. CodeQL v3 требует право
`security-events: write` для отправки результатов. Без явного указания
workflow полагается на default permissions репозитория, что может
привести к ошибке при жёстких настройках токена.

## Компромиссы (trade-offs)

| Подход | Плюсы | Минусы |
|--------|-------|--------|
| **Только обновление версий CodeQL (минимальное изменение)** | Минимальный diff, нулевой риск регрессий | checkout@v2 остаётся deprecated; workaround `HEAD^2` сохраняется |
| **Полное обновление (checkout v4 + удаление workaround + permissions)** | Чистая конфигурация, соответствие актуальным практикам | Более широкий diff, необходима проверка на PR |

**Рекомендация:** Полное обновление. Все изменения совместимы и малорисковы.
Checkout v4 и CodeQL v3 используют один и тот же Node 20 runtime, что
обеспечивает согласованность.

## Нефункциональные аспекты

- **Безопасность:** CodeQL v3 поддерживает актуальные правила анализа.
- **Поддерживаемость:** v3 — долгосрочно поддерживаемая версия.
- **Производительность:** Без изменений (autobuild, matrix по языкам).

## Связанные решения

- Нет связанных ADR.
