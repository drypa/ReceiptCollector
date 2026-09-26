# План: Актуализация GitHub Actions

**Связанный ADR:** [ADR-025](../adr/025-update-github-actions.md)
**Файл задачи:** [update-github-actions.md](../tasks/update-github-actions.md)

> **Читать перед началом.** На 2026-09-26 все три workflow уже падают: GitHub
> удалил Node 20 с раннеров 2026-09-23
> ([changelog](https://github.blog/changelog/2026-09-23-node-20-is-no-longer-available-in-github-actions/)).
> Это восстановление CI, а не рискованный рефакторинг. Зелёной базовой линии
> нет, и откат «на прежнюю версию» для 5 из 9 позиций невозможен — см.
> раздел «Лестница отката».

---

## 0. Что меняется и что не меняется

**Меняется:** 16 тегов версий в 3 файлах. Больше ничего.

**Не меняется:** триггеры `on:`, `paths`-фильтры, `if:`, команды `run:`, блоки
`with:`, порядок и количество шагов, имена шагов, таймауты, секреты, матрица
CodeQL, `permissions`, `runs-on`.

Полный диф — 36 строк: 18 удалённых и 18 добавленных, все они `uses:`.
Ни одна структурная строка workflow не затронута.

---

## 1. Правки

### 1.1 `build-analytics-image.yml` (коммит 3)

```diff
--- a/.github/workflows/build-analytics-image.yml
+++ b/.github/workflows/build-analytics-image.yml
@@ -13,10 +13,10 @@
   build:
     runs-on: ubuntu-latest
     steps:
-    - uses: actions/checkout@v4
+    - uses: actions/checkout@v7
 
     - name: Setup Node
-      uses: actions/setup-node@v4
+      uses: actions/setup-node@v7
       with:
         node-version: '22'
 
@@ -29,7 +29,7 @@
       run: npm run lint
 
     - name: Setup .NET
-      uses: actions/setup-dotnet@v4
+      uses: actions/setup-dotnet@v6
       with:
         dotnet-version: '10.0.x'
 
@@ -53,10 +53,10 @@
         docker rm mongo-test || true
 
     - name: Set up Docker Buildx
-      uses: docker/setup-buildx-action@v3
+      uses: docker/setup-buildx-action@v4
 
     - name: Login to DockerHub
-      uses: docker/login-action@v3
+      uses: docker/login-action@v4
       if: github.event_name != 'pull_request'
       with:
         username: ${{ secrets.DOCKER_USERNAME }}
@@ -64,7 +64,7 @@
 
     - name: Extract metadata for Docker
       id: meta
-      uses: docker/metadata-action@v5
+      uses: docker/metadata-action@v6
       with:
         images: drypa/receipt-collector-analytics
         tags: |
@@ -74,7 +74,7 @@
           type=raw,value=latest,enable={{is_default_branch}}
 
     - name: Build and push Docker image
-      uses: docker/build-push-action@v5
+      uses: docker/build-push-action@v7
       with:
         context: ./Analytics
         file: ./Analytics/Dockerfile
```

### 1.2 `build-backend-image.yml` (коммит 1)

```diff
--- a/.github/workflows/build-backend-image.yml
+++ b/.github/workflows/build-backend-image.yml
@@ -24,13 +24,13 @@
   build:
     runs-on: ubuntu-latest
     steps:
-    - uses: actions/checkout@v4
+    - uses: actions/checkout@v7
 
     - name: Set up Docker Buildx
-      uses: docker/setup-buildx-action@v3
+      uses: docker/setup-buildx-action@v4
 
     - name: Login to DockerHub
-      uses: docker/login-action@v3
+      uses: docker/login-action@v4
       if: github.event_name != 'pull_request'
       with:
         username: ${{ secrets.DOCKER_USERNAME }}
@@ -38,7 +38,7 @@
 
     - name: Extract metadata for Backend Docker
       id: backend-meta
-      uses: docker/metadata-action@v5
+      uses: docker/metadata-action@v6
       with:
         images: drypa/receipt-collector
         tags: |
@@ -49,7 +49,7 @@
 
     - name: Extract metadata for Telegram Bot Docker
       id: bot-meta
-      uses: docker/metadata-action@v5
+      uses: docker/metadata-action@v6
       with:
         images: drypa/receipt-telegram-bot
         tags: |
@@ -59,7 +59,7 @@
           type=raw,value=latest,enable={{is_default_branch}}
 
     - name: Build and push Backend Docker image
-      uses: docker/build-push-action@v5
+      uses: docker/build-push-action@v7
       with:
         context: .
         file: ./backend/Dockerfile
@@ -70,7 +70,7 @@
         cache-to: type=gha,mode=max
 
     - name: Build and push Telegram Bot Docker image
-      uses: docker/build-push-action@v5
+      uses: docker/build-push-action@v7
       with:
         context: .
         file: ./bot/Dockerfile
```

### 1.3 `codeql-analysis.yml` (коммит 2)

```diff
--- a/.github/workflows/codeql-analysis.yml
+++ b/.github/workflows/codeql-analysis.yml
@@ -30,7 +30,7 @@
 
     steps:
     - name: Checkout repository
-      uses: actions/checkout@v2
+      uses: actions/checkout@v7
       with:
         # We must fetch at least the immediate parents so that if this is
         # a pull request then we can checkout the head.
@@ -43,7 +43,7 @@
 
     # Initializes the CodeQL tools for scanning.
     - name: Initialize CodeQL
-      uses: github/codeql-action/init@v1
+      uses: github/codeql-action/init@v4
       with:
         languages: ${{ matrix.language }}
         # If you wish to specify custom queries, you can do so here or in a config file.
@@ -54,7 +54,7 @@
     # Autobuild attempts to build any compiled languages  (C/C++, C#, or Java).
     # If this step fails, then you should remove it and run the build manually (see below)
     - name: Autobuild
-      uses: github/codeql-action/autobuild@v1
+      uses: github/codeql-action/autobuild@v4
 
     # ℹ️ Command-line programs to run using the OS shell.
     # 📚 https://git.io/JvXDl
@@ -68,4 +68,4 @@
     #   make release
 
     - name: Perform CodeQL Analysis
-      uses: github/codeql-action/analyze@v1
+      uses: github/codeql-action/analyze@v4
```

**Сознательно НЕ меняется в этом файле:**

- `fetch-depth: 2` — валиден в `checkout@v7`, дефолт не менялся.
- шаг `git checkout HEAD^2` — см. ADR-025 §3.2. Устарел (в актуальном шаблоне
  GitHub его нет), но валиден в v4, а его удаление — запрещённое изменение
  логики пайплайна.
- `matrix.language: ['go', 'javascript']` и `fail-fast: false`.
- закомментированный `queries`.
- блок `permissions` — см. §5 контингенции.

---

## 2. Источники по каждой замене

Без ссылки на источник замена не считается выполненной.

| # | Замена | Источник, подтверждающий мажор и ломающие изменения |
|---|---|---|
| 1 | `checkout@v4 → v7` | [v5.0.0 (2025-08-11)](https://github.com/actions/checkout/releases/tag/v5.0.0) — Node 24, runner ≥ 2.327.1; [v6.0.0 (2025-11-20)](https://github.com/actions/checkout/releases/tag/v6.0.0) + [PR #2286](https://github.com/actions/checkout/pull/2286) — **BREAKING:** кредиты в отдельный файл `RUNNER_TEMP/git-credentials-*.config`, в `.git/config` остаётся только `includeIf`; [v7.0.0 (2026-06-18)](https://github.com/actions/checkout/releases/tag/v7.0.0) + [PR #2454](https://github.com/actions/checkout/pull/2454) — **BREAKING:** запрет чекаута форк-PR в `pull_request_target`/`workflow_run` (наши триггеры не затронуты); [CHANGELOG v7](https://github.com/actions/checkout/blob/v7/CHANGELOG.md) |
| 2 | `checkout@v2 → v7` | То же + подтверждение мёртвого рантайма: `action.yml`@v2 содержит `runs: using: node12` |
| 3 | `setup-node@v4 → v7` | [v5.0.0 (2025-09-04)](https://github.com/actions/setup-node/releases/tag/v5.0.0) — **BREAKING:** автокеш npm по умолчанию; [v6.0.0 (2025-10-14)](https://github.com/actions/setup-node/releases/tag/v6.0.0) — **BREAKING:** автокеш только для npm + удалён вход `always-auth`; [v7.0.0 (2026-07-14)](https://github.com/actions/setup-node/releases/tag/v7.0.0) — миграция на ESM, удаление фиктивного `NODE_AUTH_TOKEN`; [README v7 §Breaking changes in V5/V6/V7](https://github.com/actions/setup-node/blob/v7/README.md) |
| 4 | `setup-dotnet@v4 → v6` | [v5.0.0 (2025-09-03)](https://github.com/actions/setup-dotnet/releases/tag/v5.0.0) — **BREAKING:** Node 24 (runner ≥ 2.327.1) + [PR #647](https://github.com/actions/setup-dotnet/pull/647) «Remove Support for older .NET Versions» (убраны quality `signed`/`validated`); [v6.0.0 (2026-07-16)](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0) — только ESM |
| 5 | `setup-buildx-action@v3 → v4` | [v4.0.0 (2026-03-05)](https://github.com/docker/setup-buildx-action/releases/tag/v4.0.0) — Node 24 (runner ≥ 2.327.1) + **BREAKING:** [PR #464](https://github.com/docker/setup-buildx-action/pull/464) удаляет входы `config`/`config-inline`/`install` и выходы `endpoint`/`status`/`flags` |
| 6 | `login-action@v3 → v4` | [v4.0.0 (2026-03-04)](https://github.com/docker/login-action/releases/tag/v4.0.0) — Node 24 (runner ≥ 2.327.1), ESM, обновление AWS SDK. Ломающих изменений входов/выходов не заявлено |
| 7 | `metadata-action@v5 → v6` | [v6.0.0 (2026-03-05)](https://github.com/docker/metadata-action/releases/tag/v6.0.0) — Node 24 (runner ≥ 2.327.1) + [PR #607](https://github.com/docker/metadata-action/pull/607) — **BREAKING (парсинг):** `#` внутри значений списков теперь сохраняется |
| 8 | `build-push-action@v5 → v7` | [v6.0.0 (2024-06-17)](https://github.com/docker/build-push-action/releases/tag/v6.0.0) — добавлены build record и Build Summary (не ломающее); [v7.0.0 (2026-03-05)](https://github.com/docker/build-push-action/releases/tag/v7.0.0) — Node 24 (runner ≥ 2.327.1), удалены `DOCKER_BUILD_NO_SUMMARY`/`DOCKER_BUILD_EXPORT_RETENTION_DAYS`, удалён legacy `export-build`; [README v7 §inputs](https://github.com/docker/build-push-action/blob/v7/README.md) — `required: true` отсутствует, `cache-from`/`cache-to`/`push`/`tags`/`labels`/`context`/`file` без изменений |
| 9 | `codeql-action/*@v1 → v4` | [changelog 2025-10-28](https://github.blog/changelog/2025-10-28-upcoming-deprecation-of-codeql-action-v3/) — v4 выпущен 2025-10-07, Node 24, миграция сводится к замене тегов; рантаймы по мажорам прочитаны из `action.yml`: v1 `node12`, v2 `node16`, v3 `node20`, v4 `node24`; [README v4 §Workflow Permissions](https://github.com/github/codeql-action/blob/v4/README.md) — требуется `security-events: write`; [шаблон GitHub `codeql.yml`](https://github.com/actions/starter-workflows/blob/main/code-scanning/codeql.yml) — образец актуальной конфигурации |

**Проверено по исходникам, а не только по release notes:**

- `github/codeql-action` `src/autobuild.ts` → `determineAutobuildLanguages()`:
  для интерпретируемых языков возвращает `undefined` и пишет
  «None of the languages in this project require extra build steps» — `autobuild`
  для `javascript` завершается с кодом 0, а не падает. Go-автобилдер
  запускается; обратная совместимость мультиязычного autobuild в v4 сохранена
  (в коде есть явный комментарий, что она будет удалена только в следующем
  мажоре).
- `actions/setup-node` `src/main.ts` → `getNameFromPackageManagerField()`: автокеш
  включается, только если `packageManager`/`devEngines.packageManager` есть в
  `package.json` **в корне репозитория**. В нашем репозитории `package.json` в
  корне отсутствует, в `Analytics/frontend/package.json` поля нет →
  **автокеш не включится**, дополнительных правок не требуется.
- `actions/checkout` `src/unsafe-pr-checkout-helper.ts`: проверка немедленно
  выходит, если `eventName` не `pull_request_target` и не `workflow_run`.

---

## 3. Порядок применения

Три коммита, три PR, строго последовательно. Каждый следующий начинается
**только** после зелёного merge предыдущего.

### Коммит 1 / PR-1 — `build-backend-image.yml`

```bash
git switch -c chore/update-github-actions-backend
# применить §1.2
# затем вставить команду из §6 (ожидается OK, счётчики 7 / 7 / 4)
git add .github/workflows/build-backend-image.yml
git commit -m "Update Docker actions in build-backend-image.yml to current majors"
git push -u origin chore/update-github-actions-backend
```

**Проверка на push ветки:** `on.push.paths` у этого workflow включает сам
workflow-файл, а `on.push` не ограничен ветками — поэтому **push ветки уже
запускает проверку**. Идём в Actions → *Docker Image CI*.

Ожидаемо:
- шаг `Set up Docker Buildx`, `Login to DockerHub`, оба `Extract metadata`,
  оба `Build and push` — зелёные;
- в логе видны `docker/setup-buildx-action@v4`, `docker/login-action@v4`,
  `docker/metadata-action@v6`, `docker/build-push-action@v7`;
- появилась секция **Build summary** (ожидаемо, v6+; на результат не влияет);
- в DockerHub появились теги по имени ветки и по короткому SHA
  (ожидаемо: при `push` условие `if: != 'pull_request'` не срабатывает).
  `latest` НЕ должен появиться.

Затем открыть PR → проверка запустится ещё раз по `pull_request` (путь
workflow-файла есть и в `on.pull_request.paths`).

**Ожидаемая картина PR-1:** *Docker Image CI* — зелёная, **CodeQL — красная
(это норма, она была красной и до правок; чинится в PR-2)**. Не принимать CodeQL
за регрессию.

### Коммит 2 / PR-2 — `codeql-analysis.yml`

```bash
git switch master && git pull
git switch -c chore/update-github-actions-codeql
# применить §1.3
# затем вставить команду из §6
git add .github/workflows/codeql-analysis.yml
git commit -m "Update checkout and CodeQL actions in codeql-analysis.yml to current majors"
git push -u origin chore/update-github-actions-codeql
```

Push ветки CodeQL **не запустит** (`on.push.branches: [master]`). Проверка
запускается по PR: `on.pull_request.branches: [master]` без `paths`-фильтра,
то есть срабатывает всегда.

**Проверка:**
- Actions → *CodeQL*, два job матрицы (`go`, `javascript`) — зелёные;
- в логе `Autobuild` для `go` — реальная сборка; для `javascript` — строка
  «None of the languages in this project require extra build steps»;
- в логах **нет** предупреждения «CodeQL Action v3 will be deprecated in
  December 2026» (в v4 оно не выводится);
- **Security → Code scanning**: приходят алерты, категории соответствуют
  прежним.

**Ожидаемая картина PR-2:** *CodeQL* — зелёная, *Docker Image CI* — зелёная
(из PR-1), *Build Analytics Docker Image* — не запускается (фильтр путей,
см. §6).

### Коммит 3 / PR-3 — `build-analytics-image.yml`

```bash
git switch master && git pull
git switch -c chore/update-github-actions-analytics
# применить §1.1
# затем вставить команду из §6
git add .github/workflows/build-analytics-image.yml
git commit -m "Update checkout, Node, .NET and Docker actions in build-analytics-image.yml"
git push -u origin chore/update-github-actions-analytics
```

**Проверка на push ветки** (обязательный путь, см. §6): Actions →
*Build Analytics Docker Image*.

Ожидаемо:
- `Install frontend dependencies` (npm ci) — зелёный, **в логе нет строк
  `Cache restored`/`Cache saved`** — автокеш не включился (ADR-025 §2.2);
- `Lint frontend` — зелёный, вывод ESLint не изменился;
- `Run tests` (`dotnet test --configuration Release`) — зелёный;
- `Stop Docker containers` — зелёный (`if: always()`);
- Docker-шаги — зелёные, теги `branch`/`sha` в DockerHub, без `latest`.

PR-проверка этого workflow **не запустится** — путь workflow-файла не входит в
`on.pull_request.paths` (только `'Analytics/**'`). Обойти без нарушения FR-1 и
без правки кода приложения нельзя. Если нужна проверка именно в PR — вынести
выравнивание фильтров в отдельную задачу.

**Ожидаемая картина PR-3:** *CodeQL* и *Docker Image CI* — зелёные,
*Build Analytics Docker Image* — проверена на push ветки.

### Почему не один коммит

Три независимые статусные проверки; в одном PR одна красная маскирует зелёный
статус двух других. `checkout` — первый шаг во всех трёх workflow: если
несовместим он, падают все три одинаково, и по одному PR «сломался checkout» и
«сломался checkout в другом файле» неразличимы. `codeql-analysis.yml` устроен
принципиально иначе (матрица, autobuild, SARIF, права) — смешивать его с
`docker/*` значит при поломке грешить на Docker. `git revert` одного коммита =
точечный откат ровно одного workflow; с тремя workflow в одном коммите частичный
откат невозможен.

---

## 4. Если сломалась — что делать (FR-4)

### Лестница отката

Прочитано `runs.using` из `action.yml` **всех** версий. Отсюда — ключевое
ограничение FR-4: **для 5 из 9 позиций отката не существует.**

| Экшен | Цель | Откат | Комментарий |
|---|---|---|---|
| `actions/checkout` | v7 | **v6 → v5** | v5/v6/v7 — все `node24` |
| `actions/setup-node` | v7 | **v6 → v5** | v5/v6/v7 — все `node24` |
| `actions/setup-dotnet` | v6 | **v5** | v5/v6 — оба `node24` |
| `docker/setup-buildx-action` | v4 | ❌ нет | v3 = `node20` |
| `docker/login-action` | v4 | ❌ нет | v3 = `node20` |
| `docker/metadata-action` | v6 | ❌ нет | v5 = `node20` |
| `docker/build-push-action` | v7 | ❌ нет | v5 и v6 = `node20` |
| `github/codeql-action/*` | v4 | ❌ нет | v1 `node12`, v2 `node16`, v3 `node20` |

Возврат `docker/build-push-action` на `@v5` не «возвращает рабочее состояние» —
он возвращает заведомо нерабочее.

### Таблица «симптом → причина → действие»

| # | Симптом (что видно в логе) | Вероятная причина | Действие |
|---|---|---|---|
| 1 | Падение на **первом** шаге, текст вида `The 'x' action uses Node 20 / node12, which is deprecated` или `This action is deprecated` | Пин не применён (остался `@v4`/`@v5`/`@v2`) | Проверить `git diff` — замена не прошла. Применить заново |
| 2 | `Set up Docker Buildx` падает, в тексте `node24` / `Error: spawn ... ENOENT` | `setup-buildx-action@v4` несовместим с образом раннера | Отката **нет**. Сузить: временно зафиксировать `runs-on: ubuntu-24.04` в этом workflow (выход за FR-1, требует согласования) → **follow-up** |
| 3 | `Login to DockerHub` падает, `unauthorized` / `Error: Login failed` | `login-action@v4` изменил требования к аутентификации | Отката **нет**. Сначала исключить #4: убедиться, что `secrets.DOCKER_USERNAME`/`DOCKER_SECRET` на месте. Затем чинить вперёд → **follow-up** |
| 4 | На `push` образы не публикуются, тегов в DockerHub нет, при этом в логе `push: true`, `skipped` | Нарушен `if: github.event_name != 'pull_request'` или `push:` | Отката **нет**. Проверить, что `if:` и `push:` не тронуты (FR-1). Если тронуты — восстановить из `git show HEAD~1:<file>` → это регрессия нашей правки, чинится сразу |
| 5 | Теги образа другие (`latest` на ветке, отсутствует `sha`, новые префиксы) | `metadata-action@v6` изменил парсинг списков | Отката **нет**. Проверить, что блок `tags:` не содержит `#` (у нас не содержит) и что строки не потеряли отступ. Сверить фактические теги с ожидаемыми `branch`/`pr`/`sha`/`latest` |
| 6 | `Lint frontend` падает **только после** `setup-node@v7` | Включился автокеш npm (в `Analytics/frontend/package.json` появилось поле `packageManager`) | **Откат: `setup-node` → `@v6` → `@v5`.** Проверить `git diff Analytics/frontend/package.json`. Если поле добавлено намеренно — это отдельное решение, а не следствие обновления |
| 7 | `npm ci` падает с `401`/`ENEEDAUTH`, в логе нет `NODE_AUTH_TOKEN` | v7 удалил фиктивный экспорт `NODE_AUTH_TOKEN`; для публичного npm это неважно, для приватного реестра — важно | **Откат: `setup-node` → `@v6`.** Если используется приватный реестр — задача `registry-url` + токен (вне объёма) |
| 8 | `Run tests` падает: `dotnet` не найден или версия не 10.x | `setup-dotnet@v5+` убрал поддержку старых .NET; либо `dotnet-version: '10.0.x'` не разрешился | **Откат: `setup-dotnet` → `@v5`.** Проверить вывод версии `dotnet --version`. Если нужен `dotnet-quality` — в v5 допустимы только `daily`/`preview`/`ga` |
| 9 | CodeQL: `Autobuild` падает на `go` с `We were unable to automatically build your code` | Go-автобилдер v4 не справился | Отката **нет**. По инструкции GitHub — заменить на ручные build-шаги. Это правка логики → **follow-up**, частичное закрытие задачи по FR-3 |
| 10 | CodeQL: `Analyze` падает с `Resource not accessible by integration` | Токену не хватает `security-events: write` | **Контингенция §5.** Отката **нет** |
| 11 | CodeQL: анализ PR падает после удаления `HEAD^2` | Мы его не удаляли; если удалён вручную — вернуть шаг | `git checkout HEAD~1 -- .github/workflows/codeql-analysis.yml` |
| 12 | CodeQL: результаты не появились в Security → Code scanning | Загрузка SARIF не прошла | Смотреть лог `Perform CodeQL Analysis`. Если `security-events: write` есть, а SARIF не загружен — вероятен конфликт категорий/прав → **follow-up** |
| 13 | `cache-from`/`cache-to type=gha` перестал работать, ошибки кеша | Не должно быть: в v7 нет `required: true` входов, `cache-*` сквозные | Отката **нет**. Проверить, не введён ли `cache-mode` (вне объёма). Иначе → **follow-up** |
| 14 | Падает `Run tests` из-за контейнеров `postgres`/`mongo`, не из-за версий экшенов | Проблема образа/раннера, а не Actions | Сравнить с прогоном PR-1/PR-2. Если там зелёно — регрессия наша; если нет — внешняя причина |
| 15 | Любая поломка, для которой нет строки в этой таблице | — | 1) `git revert <commit>` этого workflow; 2) если revert не восстанавливает зелёное (ожидаемо для `docker/*` и CodeQL) — чинить вперёд; 3) завести follow-up; 4) зафиксировать в отчёте, какая из 9 позиций не выдержала |

**Правило отката целиком:** точечный откат конкретного экшена возможен только
для `checkout`, `setup-node`, `setup-dotnet`. Для остальных пяти — откат
экшена технически невозможен, потому что все предыдущие мажоры работают на
удалённых рантаймах. Откат до уровня workflow (`git revert` коммита) сужает
периметр до одного файла, но оставляет его в нерабочем состоянии — применять
только как последнюю меру, с обязательной записью в отчёте.

---

## 5. Контингенция: `permissions` для CodeQL

**В основном изменении блок `permissions` НЕ добавляется** (ADR-025 §3.1): v4 не
добавляет требований относительно v1, репозиторий публичный, а блок
`permissions` на уровне job обнуляет все остальные права в `none` — а
`actions/checkout` без `contents: read` падает.

Применять **только** при симптоме #10, и **только** в этой форме:

```yaml
jobs:
  analyze:
    name: Analyze
    runs-on: ubuntu-latest
    permissions:
      security-events: write   # требуется analyze для загрузки SARIF
      contents: read           # обязательно: без него actions/checkout не работает
    strategy:
      ...
```

Одна строка `security-events: write` без `contents: read` — типичная ошибка,
воспроизводящая симптом #10 с другой стороны. `packages: read` и
`actions: read` для публичного репозитория без приватных CodeQL-паков не нужны
(официальный шаблон включает их для GHES/приватных репозиториев).

---

## 6. Локальная проверка

### Вариант A — без зависимостей (основной)

Только стандартная библиотека Python 3 (`re`, `subprocess`, `sys`,
`pathlib`). Ничего не устанавливается, **ничего не добавляется в
репозиторий** — команда вставляется в терминал и живёт только в рамках сессии.

Логика: для каждого `*.yml` в `.github/workflows/` сравнивается рабочая копия
с `git show HEAD:<file>` построчно. Единственное допустимое расхождение —
изменившийся ref после `@` в строке `uses:`. Всё остальное должно совпасть
байт в байт. Это машинная гарантия того, что `on:`, `if:`, `run:`, `with:`,
`runs-on:`, `permissions:`, порядок, количество шагов и имена экшенов не
тронуты (FR-1). Дополнительно проверяется, что все `uses` запинены мажорным
тегом `@vN` — SHA-пиннинг и точечные версии запрещены «Вне объёма».

```bash
python3 - HEAD <<'PY'
import re, subprocess, sys
from pathlib import Path

USES = re.compile(r"^(?P<head>\s*(?:-\s*)?uses:\s*)(?P<act>[^@\s#]+)(?:@(?P<ref>[^#\s]+))?(?P<tail>.*)$")
MAJOR = re.compile(r"^v\d+$")

def normalize(line):
    m = USES.match(line)
    if not m:
        return line, None
    return f"{m['head']}{m['act']}{m['tail']}", m["ref"]

def check(path, base):
    rel = path.as_posix()
    try:
        old = subprocess.run(["git", "show", f"{base}:{rel}"],
                             capture_output=True, text=True, check=True).stdout.splitlines()
    except subprocess.CalledProcessError:
        return [f"{rel}: нет в {base}"]
    new = path.read_text(encoding="utf-8").splitlines()
    problems = []
    if len(old) != len(new):
        return [f"{rel}: число строк изменилось {len(old)} -> {len(new)}"]
    changes = 0
    for i, (o, n) in enumerate(zip(old, new), 1):
        no, ro = normalize(o)
        nn, rn = normalize(n)
        if o == n:
            continue
        if no != nn:
            problems.append(f"{rel}:{i}: изменена не версия экшена\n    - {o}\n    + {n}")
            continue
        changes += 1
        print(f"  {rel}:{i}: {ro} -> {rn}")
    for i, line in enumerate(new, 1):
        _, ref = normalize(line)
        if ref is None:
            continue
        if not MAJOR.match(ref):
            kind = "SHA-пиннинг" if re.fullmatch(r"[0-9a-f]{40}", ref) else "не мажорный тег"
            problems.append(f"{rel}:{i}: {kind} ({ref}) — запрещено")
    print(f"  {rel}: замен версий — {changes}, строк — {len(new)}")
    return problems

base = sys.argv[1] if len(sys.argv) > 1 else "HEAD"
print(f"Сравнение с {base}")
allp = []
for p in sorted(Path(".github/workflows").glob("*.yml")):
    allp += check(p, base)
if allp:
    print("\nПРОБЛЕМЫ:")
    for x in allp:
        print(f"  - {x}")
    sys.exit(1)
print("\nOK: изменены только версии экшенов, все запинены мажорными тегами")
PY
```

Ожидаемый вывод после применения §1 (аргумент `HEAD` — база; при желании
`python3 - master` для сравнения с `master`):

```
  .github/workflows/build-analytics-image.yml:16: v4 -> v7
  .github/workflows/build-analytics-image.yml:19: v4 -> v7
  .github/workflows/build-analytics-image.yml:32: v4 -> v6
  .github/workflows/build-analytics-image.yml:56: v3 -> v4
  .github/workflows/build-analytics-image.yml:59: v3 -> v4
  .github/workflows/build-analytics-image.yml:67: v5 -> v6
  .github/workflows/build-analytics-image.yml:77: v5 -> v7
  .github/workflows/build-analytics-image.yml: замен версий — 7, строк — 85
  .github/workflows/build-backend-image.yml: замен версий — 7, строк — 81
  .github/workflows/codeql-analysis.yml: замен версий — 4, строк — 71

OK: изменены только версии экшенов, все запинены мажорными тегами
```

Ненулевой код возврата и список проблем — если что-то сломано.

**Проверено на синтетических кейсах** (9 сценариев, все дали ожидаемый
результат): подмена команды в `run:`, подмена условия `if:`, подмена значения
в `with:`, удаление шага `git checkout HEAD^2`, добавление блока `permissions:`,
переименование экшена, SHA-пиннинг, точечный пин `@v7.0.0` — все восемь
попадают; целевой дифф из §1 — единственный проходящий случай.

### Вариант B — проверка синтаксиса YAML (опционально)

Вариант A сравнивает строки и **не валидирует синтаксис YAML** — парсер в
стандартной библиотеке Python отсутствует, и обойтись без зависимости нельзя.
Если в окружении уже есть PyYAML (в этом репозитории он есть, 6.0.1), синтаксис
проверяется одной строкой — **устанавливать ничего не нужно**:

```bash
python3 -c "import sys,yaml,pathlib; [yaml.safe_load(p.read_text()) for p in sorted(pathlib.Path('.github/workflows').glob('*.yml'))]; print('YAML: синтаксис OK')"
```

Если PyYAML нет — шаг пропускается, а не ставится: добавление зависимости
противоречит «Вне объёма». Синтаксис всё равно проверит сам Actions: файл с
битым YAML не запустится, и это видно в статусе прогона.

### Ограничения (честно)

- Локальная проверка не знает про семантику экшенов и не может поймать
  runtime-несовместимость — это делает CI.
- Сравнение построчное: перестановка шагов ловится (строки не совпадут), а
  вот косметическая правка вроде замены `'22'` на `"22"` в `with:` — тоже
  ловится, как изменение не-ref строки. Ложных срабатываний на version-only
  диффе нет.
- `actionlint` / `zizmor` не подключаются — прямо запрещено «Вне объёма».

### Что проверяется только на CI

- Реальная совместимость Node 24 (раннер, образ, ESM-загрузка).
- Работоспособность Docker-демона и buildx на конкретном образе.
- Права токена и загрузка SARIF.
- Фактические теги в DockerHub.

**Авторитетная проверка — только CI.** Локальная команда — фильтр грубых
ошибок, а не доказательство работоспособности.

---

## 7. Суперсессия документов

Выполняется вместе с коммитом 3 (или отдельным коммитом — на усмотрение
исполнителя; на CI это не влияет).

| Файл | Действие |
|---|---|
| `docs/adr/017-upgrade-codeql-actions-v3.md` | **Не удалять.** В блоке «Статус» заменить `Принято` на пометку о замене со ссылкой на ADR-025 и **явно указать, какие выводы ADR-017 не подтверждаются для v4**: (а) удаление `git checkout HEAD^2` — не выполняется, шаг остаётся; (б) добавление `permissions` — не выполняется в основном изменении. Конвенция репозитория: ADR не удаляются |
| `docs/plans/update-codeql-actions-v3.md` | **Удалить.** План на v3 неисполним: v3 работает на Node 20, который удалён. Заменяется этим планом |
| `docs/tasks/update-codeql-actions-v3.md` | **Оставить в `docs/tasks/`, пометить заменённым:** в блоке «Мета» статус `Draft` → `Заменено задачей update-github-actions.md (ADR-025)`, в разделе «Техническое решение» — ссылка на ADR-025 с оговоркой, что план на v3 больше не применим |
| `docs/tasks/update-github-actions.md` | Оставить активной; в разделе «Мета» указать `ADR-025` и `docs/plans/update-github-actions.md` |

Комментарий: план удаляется, а ADR и задача — нет, потому что ADR и задача —
записи о решениях с исторической ценностью, а план — одноразовый
исполняемый артефакт, который к тому же указывает на заведомо мёртвую версию.

---

## 8. Чек-лист критериев приёмки

### Изменения (проверяются локально, командой из §6)

- [ ] Все 9 позиций из таблицы задачи переведены на целевые мажоры
- [ ] `git diff --stat` показывает изменены только 3 файла из
      `.github/workflows/`
- [ ] Команда из §6 (вариант A) завершилась строкой `OK: изменены только
      версии экшенов`, счётчики замен — 7 / 7 / 4
- [ ] Диф содержит ровно 18 удалённых и 18 добавленных строк, и все они
      содержат `uses:`
- [ ] Ни одна строка с `on:`, `if:`, `run:`, `with:`, `name:`, `runs-on:`,
      `permissions:`, `uses: ...@vN` вне списка замен не тронута
- [ ] `fetch-depth: 2` и шаг `git checkout HEAD^2` в `codeql-analysis.yml`
      на месте
- [ ] Матрица `language: ['go', 'javascript']` и `fail-fast: false` не тронуты
- [ ] Секреты (`DOCKER_USERNAME`, `DOCKER_SECRET`) не тронуты
- [ ] Все `uses` запинены мажорным тегом, SHA-пиннинга нет
- [ ] Нет `actionlint` / `zizmor`, нет кеширования npm, нет изменений в
      Dockerfile и коде приложения
- [ ] Чужие незакоммиченные изменения (`appsettings.json`,
      `appsettings.Development.json`) не попали в коммиты
- [ ] Суперсессия документов выполнена по §7

### Поведение (проверяется на CI и вручную)

- [ ] `build-backend-image.yml` зелёный на PR и на push
- [ ] `build-analytics-image.yml` зелёный (проверка на push ветки — см. §6)
- [ ] `codeql-analysis.yml` зелёный на PR
- [ ] `Lint frontend` продолжает работать, вывод ESLint не изменился
- [ ] В логе `setup-node@v7` **нет** строк `Cache restored` / `Cache saved`
- [ ] `Run tests` (`dotnet test --configuration Release`) зелёный
- [ ] Образ `drypa/receipt-collector-analytics` собран и опубликован с тегами
      `branch` / `pr` / `sha` / `latest` (после мержа в `master`)
- [ ] Образы `drypa/receipt-collector` и `drypa/receipt-telegram-bot` собраны и
      опубликованы с теми же тегами
- [ ] `latest` не появился ни на одном тестовом пуше ветки
- [ ] Условие `if: github.event_name != 'pull_request'` сработало: на PR нет
      логина в DockerHub и нет `push`
- [ ] В логах workflow нет предупреждений о deprecated actions
- [ ] В логах CodeQL нет предупреждения «CodeQL Action v3 will be deprecated»
- [ ] Результаты анализа доступны в Security → Code scanning, категории
      SARIF соответствуют прежним
- [ ] `cache-from: type=gha` / `cache-to: type=gha,mode=max` работают (в логах
      есть `importing cache manifest` / `exporting cache`)
- [ ] Ручная проверка в DockerHub: теги и образы соответствуют ожидаемым

---

## 9. Что делать, если всё прошло

Завести follow-up (вне объёма текущей задачи):

1. **Подготовка к миграции `ubuntu-latest` → Ubuntu 26.04.** Окно 19.10–19.11.2026
   ([changelog 2026-09-17](https://github.blog/changelog/2026-09-17-ubuntu-26-generally-available-and-latest-migration/)). Тестировать на `ubuntu-26.04`; если не готовы — временно `ubuntu-24.04`. Проверить
   [список изменений образа](https://github.com/actions/runner-images/issues/14747). Наш workflow
   берёт Node и .NET через actions, поэтому из образа критичен в основном Docker.
2. **Модернизация `codeql-analysis.yml`:** удалить `git checkout HEAD^2` и
   `fetch-depth: 2`, перейти на матрицу с `build-mode:`
   (`javascript-typescript` → `none`, `go` → `autobuild`), заменить псевдоним
   `javascript` на канонический `javascript-typescript`.
3. **Выравнивание `paths`-фильтров:** добавить `.github/workflows/<файл>.yml` в
   `on.pull_request.paths` обоих build-workflow, чтобы изменение версий в
   workflow-файле запускало проверку в PR.
4. **Hardening прав:** `permissions: security-events: write` + `contents: read` в
   `codeql-analysis.yml`, чтобы не зависеть от токена по умолчанию.
