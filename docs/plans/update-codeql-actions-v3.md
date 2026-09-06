# План: Обновление CodeQL actions до v3

**Связанный ADR:** [ADR-017](../adr/017-upgrade-codeql-actions-v3.md)
**Файл задачи:** [update-codeql-actions-v3.md](../tasks/update-codeql-actions-v3.md)

## Что меняется

Файл: `.github/workflows/codeql-analysis.yml`

### Изменение 1: Checkout `@v2` → `@v4`

```yaml
# Было:
- name: Checkout repository
  uses: actions/checkout@v2
  with:
    fetch-depth: 2

# Стало:
- name: Checkout repository
  uses: actions/checkout@v4
  with:
    fetch-depth: 2
```

`fetch-depth: 2` сохраняется для совместимости. v4 полностью совместим
с этим параметром.

### Изменение 2: Удаление workaround `git checkout HEAD^2`

Удалить шаг (строки 39–42 в текущем файле):

```yaml
# УДАЛИТЬ:
    # If this run was triggered by a pull request event, then checkout
    # the head of the pull request instead of the merge commit.
    - run: git checkout HEAD^2
      if: ${{ github.event_name == 'pull_request' }}
```

**Причина:** CodeQL v3 корректно работает с merge commit из checkout.
Workaround был нужен для v1.

### Изменение 3: CodeQL init `@v1` → `@v3`

```yaml
# Было:
    - name: Initialize CodeQL
      uses: github/codeql-action/init@v1

# Стало:
    - name: Initialize CodeQL
      uses: github/codeql-action/init@v3
```

### Изменение 4: Autobuild `@v1` → `@v3`

```yaml
# Было:
    - name: Autobuild
      uses: github/codeql-action/autobuild@v1

# Стало:
    - name: Autobuild
      uses: github/codeql-action/autobuild@v3
```

### Изменение 5: Analyze `@v1` → `@v3`

```yaml
# Было:
    - name: Perform CodeQL Analysis
      uses: github/codeql-action/analyze@v1

# Стало:
    - name: Perform CodeQL Analysis
      uses: github/codeql-action/analyze@v3
```

### Изменение 6: Добавить explicit permissions (опционально)

В начало job `analyze:` добавить:

```yaml
jobs:
  analyze:
    name: Analyze
    runs-on: ubuntu-latest
    permissions:
      security-events: write
```

## Что НЕ меняется

- Триггеры (`on: push/pull_request/schedule`) — без изменений
- Matrix strategy (`language: ['go', 'javascript']`) — без изменений
- Cron schedule (`'0 0 * * 2'`) — без изменений
- Параметры `init` (`languages: ${{ matrix.language }}`) — без изменений
- `fail-fast: false` — без изменений

## Порядок выполнения

1. Применить все изменения к `.github/workflows/codeql-analysis.yml`
2. Запушить ветку с изменениями
3. Открыть PR в master — workflow должен запуститься и пройти успешно
4. Проверить в логах, что используются actions версии v3
5. Проверить в Security → Code scanning, что результаты анализа доступны
6. Слить PR

## Проверка критериев приёмки

- [ ] Три action-шага обновлены на `@v3`
- [ ] Checkout обновлён на `@v4`
- [ ] Workaround `git checkout HEAD^2` удалён
- [ ] Workflow проходит на push и PR
- [ ] В логах видно `github/codeql-action/*@v3`
- [ ] Секреты и permissions не вызывают ошибок

## Риски и митигация

| Риск | Вероятность | Митигация |
|------|-------------|-----------|
| PR-анализ сломается без `HEAD^2` | Низкая (v3 не требует) | Вернуть шаг; workflow отработает на следующем push |
| `security-events: write` недоступен | Низкая | Убрать permissions, полагаться на default token |
| Autobuild не соберёт Go | Низкая (autobuild поддерживает Go) | Заменить на ручной `go build` шаг |
