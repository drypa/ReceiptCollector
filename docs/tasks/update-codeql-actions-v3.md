# Обновление CodeQL actions на v3

## Приоритет
medium

## Проблема
Workflow CodeQL (`.github/workflows/codeql-analysis.yml`) использует
deprecated action-обёртки `github/codeql-action/*@v1` (`init`, `autobuild`,
`analyze`). GitHub прекращает поддержку v1, в перспективе текущая версия
может перестать работать или выдавать предупреждения об устаревании.

Сейчас workflow работает корректно, но используется устаревшая версия
actions, которую необходимо обновить до актуальной.

## Цель
Обновить action-шаги CodeQL с `@v1` на актуальную версию `@v3`, сохранив
текущую логику запуска workflow без изменений.

## Пользователи
- Разработчики/контрибьюторы репозитория — CodeQL-анализ при пушах и PR.
- Автоматическая CI-система GitHub Actions.

## Сценарий использования
1. Разработчик открывает PR в `master` или пушит в `master`.
2. Запускается workflow CodeQL ровно с теми же триггерами, что и сейчас
   (push в master, PR в master, еженедельный schedule).
3. Workflow инициализирует CodeQL (go и javascript), проходит autobuild и
   выполняет анализ без ошибок.
4. Результаты анализа доступны в разделе Security → Code scanning.

## Критерии приёмки
- [ ] Три action-шага CodeQL (`init`, `autobuild`, `analyze`) обновлены с
      `@v1` на `@v3`.
- [ ] Checkout обновлён с `@v2` на `@v4` (устаревший Node 12 → Node 20).
- [ ] Устаревший workaround `git checkout HEAD^2` удалён (был нужен для v1,
      v3 работает с merge commit нативно).
- [ ] Добавлено `permissions: security-events: write` (метод наименьших
      привилегий).
- [ ] Workflow CodeQL успешно проходит в CI и на пуше, и на PR.
- [ ] Текущие настройки запуска workflow (триггеры `on:`, языки анализа,
      расписание) остались прежними — они устраивают и не меняются.
- [ ] В логах workflow видно, что используются actions версии v3.
- [ ] Результаты анализа доступны в Security → Code scanning.

## Техническое решение
Полное решение описано в ADR-017 (`docs/adr/017-upgrade-codeql-actions-v3.md`)
и плане (`docs/plans/update-codeql-actions-v3.md`).

Рекомендация: выполнить «полное обновление» (все четыре изменения выше),
поскольку все они совместимы и малорисковы. Замена только CodeQL
`@v1 → @v3` (минимальный diff) допустима, но оставляет устаревшие
`checkout@v2` и workaround `HEAD^2`.

## Edge cases
- Если PR-анализ сломается после удаления `HEAD^2` — вернуть шаг
  (workflow отработает на следующем push).
- Если `security-events: write` недоступен/lишён — убрать permissions и
  полагаться на default token.
- Если autobuild не соберёт Go — заменить на ручной `go build` шаг.

## Зависимости
- Нет внешних зависимостей. Действия берутся с GitHub Marketplace.

## Мета
- Автор: drypa
- Дата создания: 2026-09-06
- Статус: Draft
