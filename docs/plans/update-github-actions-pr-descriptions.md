# Описания PR: актуализация GitHub Actions

**Связанные документы:** [ADR-025](../adr/025-update-github-actions.md) ·
[План](update-github-actions.md) · [Задача](../tasks/update-github-actions.md)

Документ содержит **три готовых к копипасте описания PR** — по одному на каждый
из трёх последовательных PR задачи. Ничего больше в нём нет: ни правок кода, ни
команд создания веток, ни новых решений. Все версии, шаги и наблюдаемые признаки
взяты из плана и ADR-025; если чего-то в них нет, оно сюда не добавлено.

## Как пользоваться

1. Открывать PR **строго по порядку**: PR-1 → merge → PR-2 → merge → PR-3.
   Каждый следующий начинается только после зелёного merge предыдущего
   (план §3, ADR-025 §6).
2. Ветки создаются из `master` командами из плана §3 (команды выполняет
   пользователь, не бот-агент):

   | PR | Ветка | Файл |
   |---|---|---|
   | PR-1 | `chore/update-github-actions-backend` | `.github/workflows/build-backend-image.yml` |
   | PR-2 | `chore/update-github-actions-codeql` | `.github/workflows/codeql-analysis.yml` |
   | PR-3 | `chore/update-github-actions-analytics` | `.github/workflows/build-analytics-image.yml` |

3. Тело PR копируется из fenced-блока соответствующего раздела **целиком**,
   включая подзаголовки.
4. **Заголовок PR** дан в стиле сообщений коммитов репозитория (короткий,
   нижний регистр, без точки). Рядом указан канонический текст коммита из
   плана §3 — если он предпочтительнее, используется он, а не заголовок.

## Локальная проверка перед коммитом (общая для всех трёх PR)

Команда из плана §6 (вариант A, только стандартная библиотека Python 3, ничего
не устанавливает и не добавляет в репозиторий) сравнивает рабочую копию каждого
`*.yml` в `.github/workflows/` с `git show HEAD:<file>` построчно и допускает
единственное расхождение — изменившийся ref после `@` в строке `uses:`.

Ожидаемые счётчики, если запускать её **до коммита** соответствующего PR, на
ветке, отведённой от `master` с уже смерженными предыдущими PR:

| Состояние | `build-analytics-image.yml` | `build-backend-image.yml` | `codeql-analysis.yml` |
|---|---|---|---|
| PR-1 применён | 0 | **7** | 0 |
| PR-1 + PR-2 применены | 0 | 0 | **4** |
| все три применены (финал) | **7** | 0 | 0 |

Итоговая строка `OK: изменены только версии экшенов, все запинены мажорными
тегами` обязана присутствовать, список `ПРОБЛЕМЫ` — отсутствовать. (Счётчики
7 / 7 / 4 из плана §6 относятся к сравнению всех трёх workflow с базовым
коммитом задачи, а не к состоянию внутри отдельного PR.)

---

# PR-1

**Ветка:** `chore/update-github-actions-backend`

**Заголовок PR:** `update docker actions in build-backend-image workflow`
*(канонический коммит из плана §3: `Update Docker actions in build-backend-image.yml to current majors`)*

**Тело PR — копипаст:**

````markdown
Часть 1 из 3 задачи `update-github-actions`. ADR-025. Это восстановление CI,
а не рефакторинг: зелёной базовой линии сейчас нет.

## Что сломано и почему

2026-09-23 GitHub окончательно отключил Node 20 в GitHub Actions
([changelog](https://github.blog/changelog/2026-09-23-node-20-is-no-longer-available-in-github-actions/)):
раннеры выполняют JavaScript-экшены только на Node 24, а флаг
`ACTIONS_ALLOW_USE_UNSECURE_NODE_VERSION` больше не работает.

Все экшены этого workflow работают на `node20`
(`runs.using` в `action.yml` соответствующих тегов), поэтому пайплайн падает на
первом action-шаге. Ни сборка, ни публикация образов бэка и Telegram-бота сейчас
не работают.

## Что изменено

Ровно 7 замен версий в 1 файле, 7 добавлений / 7 удалений строк — все они
`uses:`. Больше ничего.

| Экшен | Было | Стало | Раз |
|---|---|---|---|
| `actions/checkout` | v4 | v7 | 1 |
| `docker/setup-buildx-action` | v3 | v4 | 1 |
| `docker/login-action` | v3 | v4 | 1 |
| `docker/metadata-action` | v5 | v6 | 2 |
| `docker/build-push-action` | v5 | v7 | 2 |

Целевые мажоры подтверждены по GitHub Releases API на 2026-09-26
(ADR-025 §1), ломающие изменения каждого мажора разобраны в ADR-025 §2.

## Что НЕ изменено

Ничего, кроме ref после `@` в строках `uses:`:

- триггеры `on:`, оба фильтра `paths:`, условие
  `if: github.event_name != 'pull_request'`;
- команды `run:` (их в этом файле нет), блоки `with:` целиком, включая
  `push: ${{ github.event_name != 'pull_request' }}`, `cache-from: type=gha`,
  `cache-to: type=gha,mode=max` и блоки `tags:`;
- порядок, количество, имена и `id` шагов, `runs-on: ubuntu-latest`;
- секреты `DOCKER_USERNAME` / `DOCKER_SECRET`;
- блок `permissions` не добавлялся — v4/v6/v7 новых требований не вводят
  (ADR-025 §3.1);
- Dockerfile и код приложения не тронуты.

Побочный эффект, ожидаемый и не влияющий на результат: `build-push-action` v6+
пишет секцию **Build summary** в сводку проверки (ADR-025 §2.7).

## Как проверить

**Проверка на push ветки.** У этого workflow `on.push.paths` включает сам
workflow-файл, а `on.push` не ограничен ветками — обычный push ветки уже
запускает проверку. Actions → *Docker Image CI*.

Ожидаемо зелёные шаги: `Set up Docker Buildx`, `Login to DockerHub`,
`Extract metadata for Backend Docker`, `Extract metadata for Telegram Bot Docker`,
`Build and push Backend Docker image`, `Build and push Telegram Bot Docker image`.

Наблюдаемые признаки в логе:

- в шагах видны `docker/setup-buildx-action@v4`, `docker/login-action@v4`,
  `docker/metadata-action@v6`, `docker/build-push-action@v7`;
- нет ни одного предупреждения о deprecated actions / удалённом Node 20.

Наблюдаемые признаки в DockerHub (для `drypa/receipt-collector` и
`drypa/receipt-telegram-bot`):

- появились теги по имени ветки и по короткому SHA — это ожидаемо: при
  `github.event_name == 'push'` условие `if: ... != 'pull_request'` не
  срабатывает, логин и `push` происходят;
- тег `latest` НЕ появился (`enable={{is_default_branch}}` на ветке ложно).

Прогон по `pull_request` (путь workflow-файла есть и в `on.pull_request.paths`)
запустится ещё раз: там `push` равен `false`, логина в DockerHub нет, образы
не публикуются. Это тоже норма.

## Ожидаемая картина статусных проверок на этом PR

| Проверка | Ожидается | Почему |
|---|---|---|
| Docker Image CI | 🟢 зелёная | смысл PR |
| CodeQL | 🔴 красная | **это норма, а не регрессия.** Она была красной и до этого PR: `codeql-analysis.yml` остаётся на `checkout@v2` и `codeql-action@v1`, оба на удалённых рантаймах. Чинится в PR-2 |
| Build Analytics Docker Image | ⏭ не запустится | `on.pull_request.paths` этого workflow содержит только `Analytics/**` |

Красную CodeQL на этом PR нельзя считать регрессией и нельзя использовать как
повод откатывать PR-1. Регрессией считается только красная *Docker Image CI*.

## Если что-то сломалось

Чинить вперёд. Точечный откат возможен только для `actions/checkout`
(v7 → v6 → v5, все три на `node24`); для всех `docker/*` рабочего отката
**не существует**: v3/v5/v6 — это `node20`, рантайм удалён (ADR-025 §5,
FR-4). Возврат `docker/build-push-action` на `@v5` возвращает заведомо
нерабочее состояние, а не рабочее.

Ориентироваться на симптомы 1–5 и 13 из [плана §4](update-github-actions.md)
(«симптом → причина → действие»): падение на первом шаге = пин не применён;
падение `Set up Docker Buildx` = чинить вперёд, follow-up; `unauthorized` в
`Login to DockerHub` = сперва проверить секреты; не публикуются образы =
проверить, что `if:` и `push:` не тронуты; «не те теги» = проверить блок
`tags:` (в нём нет `#`).
````

---

# PR-2

**Ветка:** `chore/update-github-actions-codeql`

**Заголовок PR:** `update checkout and codeql actions to current majors`
*(канонический коммит из плана §3: `Update checkout and CodeQL actions in codeql-analysis.yml to current majors`)*

**Тело PR — копипаст:**

````markdown
Часть 2 из 3 задачи `update-github-actions`. ADR-025.

## Что сломано и почему

2026-09-23 GitHub окончательно отключил Node 20 в GitHub Actions
([changelog](https://github.blog/changelog/2026-09-23-node-20-is-no-longer-available-in-github-actions/)).
Раннеры выполняют JavaScript-экшены только на Node 24.

Этот workflow был нерабочим ещё дольше: `actions/checkout@v2` работает на
`node12` (удалён с 2023), а `github/codeql-action/*@v1` — тоже на `node12`
(позже GitHub принудительно поднял его до `node20`, поэтому оставшаяся поломка
проявилась именно 23.09.2026). Code scanning не работает вообще.

## Что изменено

Ровно 4 замены версий в 1 файле, 4 добавления / 4 удаления строк — все они
`uses:`. Больше ничего.

| Экшен | Было | Стало | Раз |
|---|---|---|---|
| `actions/checkout` | v2 | v7 | 1 |
| `github/codeql-action/init` | v1 | v4 | 1 |
| `github/codeql-action/autobuild` | v1 | v4 | 1 |
| `github/codeql-action/analyze` | v1 | v4 | 1 |

v4 — единственная рабочая версия CodeQL Action: рантаймы по мажорам
v1 `node12`, v2 `node16`, v3 `node20`, v4 `node24`
([changelog 2025-10-28](https://github.blog/changelog/2025-10-28-upcoming-deprecation-of-codeql-action-v3/);
v3 устаревает в декабре 2026). Официальная инструкция по миграции сводится к
замене тегов.

## Что НЕ изменено

- триггеры `on:` (`push` только по `master`, `pull_request` по `master`,
  `schedule`), `runs-on: ubuntu-latest`, `strategy`, `fail-fast: false`,
  матрица `language: ['go', 'javascript']`, закомментированный `queries`;
- **`git checkout HEAD^2` и `fetch-depth: 2` намеренно оставлены.** В актуальном
  шаблоне GitHub их нет, но удаление шага — это изменение логики пайплайна, а
  `fetch-depth: 2` и `HEAD^2` валидны в v4. Шаг заставляет анализировать head
  ветки PR, а не merge-коммит, что стабилизирует категории SARIF
  (ADR-025 §3.2). Выносится в отдельную задачу;
- блок `permissions` **не добавлялся**: v4 не добавляет требований относительно
  v1, репозиторий публичный, а блок `permissions` на уровне job обнулял бы
  остальные права в `none` и сломал бы `actions/checkout`
  (ADR-025 §3.1);
- порядок, имена и количество шагов, команды `run:`.

## Как проверить

**Push ветки эту проверку НЕ запускает.** В `on.push.branches` указан только
`master`. Проверка идёт **по PR**: `on.pull_request.branches: [master]` без
`paths`-фильтра, то есть срабатывает всегда. Смотреть: Actions → *CodeQL*.

Ожидаемо зелёные: оба job матрицы — `Analyze (go)` и `Analyze (javascript)`.

Наблюдаемые признаки в логе:

- в логе `Autobuild` для `go` — реальная сборка Go-кода;
- в логе `Autobuild` для `javascript` — строка
  «None of the languages in this project require extra build steps»
  (для интерпретируемого языка autobuild в v4 корректно завершается с кодом 0,
  ADR-025 §3.3);
- в логах **нет** предупреждения «CodeQL Action v3 will be deprecated in
  December 2026» — в v4 оно не выводится.

Наблюдаемые признаки в интерфейсе: **Security → Code scanning** — приходят
алерты, и категории SARIF соответствуют прежним (это и есть главный смысл
сохранения `HEAD^2`).

## Ожидаемая картина статусных проверок на этом PR

| Проверка | Ожидается | Почему |
|---|---|---|
| CodeQL | 🟢 зелёная | смысл PR |
| Docker Image CI | 🟢 зелёная | из PR-1, этот файл в `paths` не входит, но workflow зелёный после PR-1 |
| Build Analytics Docker Image | ⏭ не запустится | `on.pull_request.paths` содержит только `Analytics/**` |

## Если что-то сломалось

Отката нет ни для одного экшена этого файла: `checkout@v2` — `node12`,
`codeql-action` v1/v2/v3 — `node12`/`node16`/`node20`, все эти рантаймы удалены
(ADR-025 §5, FR-4). **Чинить вперёд** — это единственный путь к зелёному статусу.
Ориентироваться на симптомы 9–12 из [плана §4](update-github-actions.md):

- `Autobuild` падает на `go` с «We were unable to automatically build your
  code» — заменить на ручные build-шаги; это правка логики, значит follow-up и
  частичное закрытие задачи (FR-3);
- `Analyze` падает с `Resource not accessible by integration` — контингенция:
  добавить в job блок `permissions` с **двумя** строками,
  `security-events: write` и `contents: read` (вторая обязательна, иначе
  сломается checkout). Точный блок — план §5;
- результаты не появились в Security → Code scanning — смотреть лог
  `Perform CodeQL Analysis`;
- анализ PR сломался из-за отсутствия `HEAD^2` — если шаг удалён вручную,
  вернуть его: `git checkout HEAD~1 -- .github/workflows/codeql-analysis.yml`.
````

---

# PR-3

**Ветка:** `chore/update-github-actions-analytics`

**Заголовок PR:** `update analytics workflow actions to current majors`
*(канонический коммит из плана §3: `Update checkout, Node, .NET and Docker actions in build-analytics-image.yml`)*

**Тело PR — копипаст:**

````markdown
Часть 3 из 3 задачи `update-github-actions`. ADR-025. Последний PR: здесь
единственный workflow, который ставит Node и .NET и выполняет `npm run lint` и
`dotnet test`.

## Что сломано и почему

2026-09-23 GitHub окончательно отключил Node 20 в GitHub Actions
([changelog](https://github.blog/changelog/2026-09-23-node-20-is-no-longer-available-in-github-actions/)).
Раннеры выполняют JavaScript-экшены только на Node 24.

Все пять экшенов этого workflow — на `node20`, пайплайн падает на первом шаге.
Линтер фронтенда и тесты аналитики в CI не запускаются, образ
`drypa/receipt-collector-analytics` не собирается и не публикуется.

## Что изменено

Ровно 7 замен версий в 1 файле, 7 добавлений / 7 удалений строк — все они
`uses:`. Больше ничего.

| Экшен | Было | Стало | Раз |
|---|---|---|---|
| `actions/checkout` | v4 | v7 | 1 |
| `actions/setup-node` | v4 | v7 | 1 |
| `actions/setup-dotnet` | v4 | v6 | 1 |
| `docker/setup-buildx-action` | v3 | v4 | 1 |
| `docker/login-action` | v3 | v4 | 1 |
| `docker/metadata-action` | v5 | v6 | 1 |
| `docker/build-push-action` | v5 | v7 | 1 |

Целевые мажоры подтверждены по GitHub Releases API на 2026-09-26
(ADR-025 §1), ломающие изменения каждого мажора разобраны в ADR-025 §2.

## Что НЕ изменено

- триггеры `on:` и оба фильтра `paths:`, условие
  `if: github.event_name != 'pull_request'`, `if: always()` у очистки
  контейнеров;
- команды `run:` целиком: `npm ci --no-audit --no-fund`, `npm run lint`,
  запуск `postgres-test` / `mongo-test`, `dotnet test --configuration Release`,
  остановка и удаление контейнеров;
- блоки `with:` целиком, включая `node-version: '22'`,
  `dotnet-version: '10.0.x'`, `push:`, `cache-from: type=gha`,
  `cache-to: type=gha,mode=max`, блок `tags:`, `working-directory`;
- порядок, количество, имена и `id` шагов, `runs-on: ubuntu-latest`;
- секреты `DOCKER_USERNAME` / `DOCKER_SECRET`;
- блок `permissions` не добавлялся (ADR-025 §3.1);
- **кеширование npm не добавлялось.** `setup-node@v5+` умеет включать автокеш
  автоматически, но у нас он не включится: автокеш требует поля
  `packageManager`/`devEngines.packageManager` со значением `npm` в
  `package.json` **в корне репозитория**, а в корне такого файла нет
  (ADR-025 §2.2);
- Dockerfile и код приложения не тронуты.

## Как проверить

**Проверку этого workflow в PR не запустить.** Путь к workflow-файлу не входит
в `on.pull_request.paths` (там только `'Analytics/**'`), поэтому PR, меняющий
только версии в `.github/workflows/build-analytics-image.yml`, эту проверку не
запустит. Обойти это, не меняя триггеры и не трогая код приложения, нельзя;
выравнивание `paths`-фильтров вынесено в отдельную задачу.

**Поэтому проверка обязана пройти на push ветки.** `on.push.paths` этот файл
включает, а `on.push` не ограничен ветками. Смотреть: Actions →
*Build Analytics Docker Image*.

Ожидаемо зелёные шаги: `Install frontend dependencies`, `Lint frontend`,
`Setup .NET`, `Start Docker containers for tests`, `Run tests`,
`Stop Docker containers` (`if: always()`), `Set up Docker Buildx`,
`Login to DockerHub`, `Extract metadata for Docker`, `Build and push Docker image`.

Наблюдаемые признаки в логе:

- в логе `setup-node` **нет** строк `Cache restored` / `Cache saved` —
  автокеш не включился, это ожидаемо (см. выше). Появление этих строк означало
  бы включение кеша npm, что запрещено задачей;
- в шагах видны `docker/setup-buildx-action@v4`, `docker/login-action@v4`,
  `docker/metadata-action@v6`, `docker/build-push-action@v7`;
- вывод `npm run lint` не изменился;
- в логах нет предупреждений о deprecated actions.

Наблюдаемые признаки в DockerHub (для `drypa/receipt-collector-analytics`):

- появились теги по имени ветки и по короткому SHA — ожидаемо: при
  `github.event_name == 'push'` условие `if: ... != 'pull_request'` не
  срабатывает, логин и `push` происходят;
- тег `latest` НЕ появился (`enable={{is_default_branch}}` на ветке ложно).

## Ожидаемая картина статусных проверок на этом PR

| Проверка | Ожидается | Почему |
|---|---|---|
| CodeQL | 🟢 зелёная | из PR-2, файл в `paths` не входит |
| Docker Image CI | 🟢 зелёная | из PR-1, файл в `paths` не входит |
| Build Analytics Docker Image | ⏭ в PR не запустится | путь workflow-файла не входит в `on.pull_request.paths`; проверяется на push ветки |

Отсутствие зелёной статусной проверки Build Analytics у этого PR — ожидаемое
поведение триггеров, а не ошибка сборки.

## Если что-то сломалось

Здесь, в отличие от PR-1 и PR-2, рабочий точечный откат **есть** у трёх
экшенов: `actions/checkout` v7 → v6 → v5, `actions/setup-node` v7 → v6 → v5,
`actions/setup-dotnet` v6 → v5 (все эти мажоры на `node24`). Для всех
`docker/*` отката нет: v3/v5/v6 — `node20`, рантайм удалён (ADR-025 §5, FR-4).

Ориентироваться на симптомы 5–8, 13 и 14 из
[плана §4](update-github-actions.md):

- `Lint frontend` падает только после `setup-node@v7` — откатить
  `setup-node` до `@v6`, затем `@v5`, и проверить `git diff
  Analytics/frontend/package.json`: если поле `packageManager` добавлено
  намеренно, это отдельное решение, а не следствие обновления;
- `npm ci` падает с `401`/`ENEEDAUTH` — v7 удалил фиктивный экспорт
  `NODE_AUTH_TOKEN`; для публичного npm неважно, для приватного реестра —
  откатить `setup-node` до `@v6` и завести задачу на `registry-url` (вне объёма);
- `Run tests` падает, `dotnet` не найден или версия не 10.x — откатить
  `setup-dotnet` до `@v5`, проверить `dotnet --version`;
- падение `Run tests` из-за контейнеров `postgres`/`mongo`, а не версий
  экшенов — сравнить с зелёными прогонами PR-1/PR-2: если там зелёно, это
  регрессия; если нет — внешняя причина;
- всё остальное, для чего отката нет, — чинить вперёд, завести follow-up и
  указать в отчёте, какая из позиций не выдержала.
````

---

## Что осталось за пределами трёх PR

Согласно плану §7 и §9, эти работы **не входят** ни в один из трёх PR и
заводятся отдельно:

- суперсессия документов: пометка ADR-017 как заменённого (с указанием, что
  выводы про удаление `HEAD^2` и добавление `permissions` не подтверждаются для
  v4), удаление `docs/plans/update-codeql-actions-v3.md`, пометка задачи
  `update-codeql-actions-v3.md` как заменённой, ссылка на ADR-025 в
  `docs/tasks/update-github-actions.md`;
- подготовка к миграции `ubuntu-latest` → Ubuntu 26.04 (окно 19.10–19.11.2026);
- модернизация `codeql-analysis.yml` (удаление `HEAD^2` и `fetch-depth: 2`,
  матрица `build-mode`, канонический идентификатор `javascript-typescript`);
- выравнивание `on.pull_request.paths` и `on.push.paths` в build-workflow;
- hardening прав: `permissions: security-events: write` + `contents: read`.
