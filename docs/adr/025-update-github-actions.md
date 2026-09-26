# ADR-025: Актуализация GitHub Actions во всех workflow

## Статус

Принято

## Контекст

### Масштаб

В `.github/workflows/` ровно три файла, и **ни один** action в них не использует
актуальный мажор. Отставание — от 1 до 5 мажоров.

| Action | Где используется | Сейчас | Актуально | Отставание | Рантайм сейчас |
|---|---|---|---|---|---|
| `actions/checkout` | `codeql-analysis.yml` | v2 | v7 | 5 | `node12` |
| `actions/checkout` | `build-analytics-image.yml`, `build-backend-image.yml` | v4 | v7 | 3 | `node20` |
| `actions/setup-node` | `build-analytics-image.yml` | v4 | v7 | 3 | `node20` |
| `actions/setup-dotnet` | `build-analytics-image.yml` | v4 | v6 | 2 | `node20` |
| `docker/setup-buildx-action` | оба build-workflow | v3 | v4 | 1 | `node20` |
| `docker/login-action` | оба build-workflow | v3 | v4 | 1 | `node20` |
| `docker/metadata-action` | оба build-workflow | v5 | v6 | 1 | `node20` |
| `docker/build-push-action` | оба build-workflow | v5 | v7 | 2 | `node20` |
| `github/codeql-action` (`init`, `autobuild`, `analyze`) | `codeql-analysis.yml` | v1 | v4 | 3 | `node12` |

Актуальные мажоры подтверждены по GitHub Releases API на 2026-09-26
(`actions/checkout` v7.0.1, `actions/setup-node` v7.0.0, `actions/setup-dotnet`
v6.0.0, `docker/setup-buildx-action` v4.4.1, `docker/login-action` v4.6.0,
`docker/metadata-action` v6.2.0, `docker/build-push-action` v7.4.0,
`github/codeql-action` v4.38.2). Колонка «рантайм сейчас» получена чтением
`runs.using` из `action.yml` соответствующих тегов.

### Критический факт: CI уже нерабочий

Заявленное в задаче «сейчас пайплайны работают, но это временная удача» —
фактически уже неверно. **2026-09-23 GitHub окончательно отключил Node 20 на
раннерах** ([changelog 2026-09-23](https://github.blog/changelog/2026-09-23-node-20-is-no-longer-available-in-github-actions/)):
раннеры выполняют JavaScript-экшены только на Node 24, а флаг
`ACTIONS_ALLOW_USE_UNSECURE_NODE_VERSION` больше не работает.

Следствие: **все три workflow падают на первом action-шаге прямо сейчас.**

| Workflow | Первый экшен | Его рантайм | Состояние |
|---|---|---|---|
| `build-analytics-image.yml` | `actions/checkout@v4` | `node20` | ❌ падает |
| `build-backend-image.yml` | `actions/checkout@v4` | `node20` | ❌ падает |
| `codeql-analysis.yml` | `actions/checkout@v2` | `node12` | ❌ падает (Node 12 отключён с 2023) |

Это меняет характер задачи: это не рискованный рефакторинг работающего пайплайна,
а **восстановление CI**. Зелёной базовой линии нет, и её нельзя защищать. Все
рассуждения ниже построены на этом факте, а не на предположении «всё работает».

### Второй независимый миграционный риск: `ubuntu-latest`

**Между 2026-10-19 и 2026-11-19** метка `ubuntu-latest` мигрирует с Ubuntu 24.04
на Ubuntu 26.04 ([changelog 2026-09-17](https://github.blog/changelog/2026-09-17-ubuntu-26-generally-available-and-latest-migration/)),
и GitHub прямо предупреждает, что это может сломать сборки, зависящие от
изменившихся инструментов. Это **вторая, не связанная с Actions** причина
поломки CI, которая придёт в течение 3–8 недель.

Практический вывод для планирования: выполнять обновление экшенов **до**
октября — правильно, потому что это разделяет два риска во времени. Если бы
обновление откладывалось на ноябрь, поломку нельзя было бы однозначно
атрибутировать. Отдельная задача на `ubuntu-26.04` обязательна (см. «Связанные
решения»).

## Решение

### 1. Целевые версии

Все девять позиций переводятся на целевые мажоры единой заменой тегов. Ничего
больше не меняется: ни триггеры `on:`, ни `if:`, ни команды, ни секреты, ни
порядок и количество шагов, ни `permissions`.

| Файл | Action | Было | Стало |
|---|---|---|---|
| `build-analytics-image.yml` | `actions/checkout` | v4 | **v7** |
| `build-analytics-image.yml` | `actions/setup-node` | v4 | **v7** |
| `build-analytics-image.yml` | `actions/setup-dotnet` | v4 | **v6** |
| `build-analytics-image.yml` | `docker/setup-buildx-action` | v3 | **v4** |
| `build-analytics-image.yml` | `docker/login-action` | v3 | **v4** |
| `build-analytics-image.yml` | `docker/metadata-action` | v5 | **v6** |
| `build-analytics-image.yml` | `docker/build-push-action` | v5 | **v7** |
| `build-backend-image.yml` | `actions/checkout` | v4 | **v7** |
| `build-backend-image.yml` | `docker/setup-buildx-action` | v3 | **v4** |
| `build-backend-image.yml` | `docker/login-action` | v3 | **v4** |
| `build-backend-image.yml` | `docker/metadata-action` (×2) | v5 | **v6** |
| `build-backend-image.yml` | `docker/build-push-action` (×2) | v5 | **v7** |
| `codeql-analysis.yml` | `actions/checkout` | v2 | **v7** |
| `codeql-analysis.yml` | `github/codeql-action/init` | v1 | **v4** |
| `codeql-analysis.yml` | `github/codeql-action/autobuild` | v1 | **v4** |
| `codeql-analysis.yml` | `github/codeql-action/analyze` | v1 | **v4** |

Итого **18 замен тегов** в 3 файлах (9 позиций таблицы задачи дают 18
фактических правок, потому что `metadata-action` и `build-push-action` в
`build-backend-image.yml` встречаются по два раза, а `checkout`,
`setup-buildx-action` и `login-action` — сразу в двух файлах), 0 структурных
изменений.

### 2. Ломающие изменения по каждому экшену

Ниже — только то, что реально ломает поведение, с проверкой по исходным
мажорным релизам. Версии без ломающих изменений помечены явно, чтобы
отсутствие риска не выглядело как непроверенное допущение.

#### 2.1 `actions/checkout` v2 → v7 и v4 → v7

Пересекаются три мажора, каждый со своим изменением.

**v5.0.0 (2025-08-11) — рантайм.** Переход `node20` → `node24`.
Требование: **runner ≥ v2.327.1** ([release notes v5.0.0](https://github.com/actions/checkout/releases/tag/v5.0.0)).
GitHub-hosted раннеры обновляются автоматически, требование выполнено.

**v6.0.0 (2025-11-20) — КРЕДИТЫ ПИШУТСЯ В ОТДЕЛЬНЫЙ ФАЙЛ.** Это самое
неочевидное ломающее изменение в задаче. PR [#2286](https://github.com/actions/checkout/pull/2286)
переносит `http.<url>.extraheader` из `.git/config` в отдельный файл
`git-credentials-*.config` в `RUNNER_TEMP`, а в `.git/config` остаются только
`includeIf.gitdir:` / `include.path`-ссылки.

- *Что может сломаться:* любой код, который читает `.git/config` в поисках
  `extraheader`, чтобы извлечь токен.
- *Затронуты ли мы:* **нет**. Ни один из трёх workflow не делает `git push`, не
  использует submodules, не парсит `.git/config`. `git fetch`/`clone` внутри
  job продолжают работать, потому что git следует `include`-директивам.
  В `build-analytics-image.yml` и `build-backend-image.yml` checkout вообще
  безымянный (`- uses:`) и это единственный шаг репозитория.

**v7.0.0 (2026-06-18) — ЗАПРЕТ НА ЧЕК-ОУТ ФОРКА В ДОВЕРЕННЫХ КОНТЕКСТАХ.**
PR [#2454](https://github.com/actions/checkout/pull/2454) добавляет проверку
`assertSafePrCheckout`, которая бросает ошибку, если workflow запущен в
`pull_request_target` или `workflow_run` и пытается зачекаутить код форка.
Разбор исходника (`src/unsafe-pr-checkout-helper.ts`) подтверждает: функция
начинается с

```ts
if (eventName !== 'pull_request_target' && eventName !== 'workflow_run') {
  return
}
```

- *Затронуты ли мы:* **нет**. Наши триггеры — `push`, `pull_request`,
  `schedule`. Ни `pull_request_target`, ни `workflow_run` не используется, так
  что проверка всегда выходит сразу. Новый opt-in
  `allow-unsafe-pr-checkout` добавлять не нужно.

Дополнительно в v7.0.1 есть три фикса, уместных здесь: «escape values passed to
`--unset`» и «trim only ascii whitespace for branch» — улучшают устойчивость
checkout'а.

**`fetch-depth` по умолчанию** не менялся ни в одном из мажоров v5–v7: дефолт
остаётся `1` (shallow). В `codeql-analysis.yml` стоит явный `fetch-depth: 2`,
он сохраняется. Поведение при работе с merge-коммитами также не менялось:
checkout по-прежнему чекаутит merge-коммит PR, если не задан `ref`.

#### 2.2 `actions/setup-node` v4 → v7

**v5.0.0 (2025-09-04) — АВТОМАТИЧЕСКОЕ КЕШИРОВАНИЕ NPM.** Заявлено как
Breaking Changes: «Enabled caching by default with package manager detection if
no cache input is provided» ([release notes v5.0.0](https://github.com/actions/setup-node/releases/tag/v5.0.0)).
Отключается через `package-manager-cache: false`.

**v6.0.0 (2025-10-14) — два breaking-изменения:**
1. автокеш включается только для npm (yarn/pnpm выключены);
2. **удалён вход `always-auth`**.

**v7.0.0 (2026-07-14) — миграция на ESM**, удалён фиктивный экспорт
`NODE_AUTH_TOKEN`, `@actions/cache` обновлён до 5.1.0, добавлены outputs
`cache-primary-key` / `cache-matched-key`.

**Ключевой вопрос: включится ли автокеш у нас?** Проверено по исходнику
(`src/main.ts`, `getNameFromPackageManagerField()`): автокеш активируется, только
если в `package.json` **в корне репозитория** (`process.env.GITHUB_WORKSPACE`)
есть поле `packageManager` или `devEngines.packageManager` со значением `npm`.
Функция читает путь жёстко, без рекурсивного поиска, и при `ENOENT` возвращает
`undefined`.

Факты по репозиторию: `package.json` в корне **отсутствует**; в
`Analytics/frontend/package.json` поля `packageManager` и `engines` **нет**
(проверено); `package-lock.json` лежит в `Analytics/frontend/`, а не в корне.

**Вывод: автокеш не включится.** Это позволяет выполнить требование «не
добавлять кеширование npm» без единого дополнительного изменения — граница
объёма соблюдена естественно. Дополнительно: даже если бы поле появилось,
кеш искал бы `package-lock.json` в корне и не нашёл бы его, а
`cache-dependency-path` мы не задаём.

Остальное безопасно: `always-auth` не используется, `registry-url` не задан
(значит `configAuthentication` не вызывается), приватных реестров нет,
`node-version: '22'` — обычный канал, его разрешение не менялось.

#### 2.3 `actions/setup-dotnet` v4 → v6

**v5.0.0 (2025-09-03) — Breaking:** переход на Node 24 (runner ≥ v2.327.1) и
«Remove Support for older .NET Versions and Update installers scripts»
(PR [#647](https://github.com/actions/setup-dotnet/pull/647)).

Что именно убрали — установлено из диффа PR: в `install-dotnet.ps1` из
допустимых значений `dotnet-quality` убраны `signed` и `validated`, а
поддерживаемые каналы в примерах смещены с 3.1/5.0/6.0 на 8.0/9.0.

- *Затронуты ли мы:* **нет**. `dotnet-quality` не используется, версия
  `dotnet-version: '10.0.x'` — актуальный GA-канал, попадает в поддерживаемый
  диапазон. `cache` не задан, а он и так по умолчанию выключен
  («caching is turned off by default»), поэтому кеш NuGet самопроизвольно не
  появится.

**v6.0.0 (2026-07-16) — только миграция на ESM** и обновление зависимостей.
Breaking-изменений не заявлено.

#### 2.4 `docker/setup-buildx-action` v3 → v4

[v4.0.0 (2026-03-05)](https://github.com/docker/setup-buildx-action/releases/tag/v4.0.0):
Node 24 по умолчанию (runner ≥ v2.327.1), миграция на ESM и — важное —
**удаление deprecated-входов и выходов** (PR [#464](https://github.com/docker/setup-buildx-action/pull/464)).

Удалены: входы `config`, `config-inline`, `install`; выходы `endpoint`,
`status`, `flags`.

- *Затронуты ли мы:* **нет**. Шаг `Set up Docker Buildx` в обоих workflow идёт
  вообще без блока `with:` и его выходы никем не читаются. Удаление `install`
  также безопасно: оно означало «сделать `docker build` алиасом
  `docker buildx build`», а мы вызываем сборку только через
  `build-push-action`, который сам передаёт `--builder`.

#### 2.5 `docker/login-action` v3 → v4

[v4.0.0 (2026-03-04)](https://github.com/docker/login-action/releases/tag/v4.0.0):
Node 24 по умолчанию (runner ≥ v2.327.1), миграция на ESM, обновление AWS SDK.
Ломающих изменений входов/выходов **не заявлено**.

- *Сохраняется ли защита `if: github.event_name != 'pull_request'`?* **Да.**
  Условие `if:` вычисляет раннер **до** запуска экшена, это механизм
  workflow-движка, а не поведение экшена. Ни v3, ни v4 не могут его обойти.
  Входы `username` / `password` не менялись, поэтому передача
  `secrets.DOCKER_USERNAME` / `secrets.DOCKER_SECRET` работает как раньше.

#### 2.6 `docker/metadata-action` v5 → v6

[v6.0.0 (2026-03-05)](https://github.com/docker/metadata-action/releases/tag/v6.0.0):
Node 24 по умолчанию (runner ≥ v2.327.1), миграция на ESM и изменение разбора
списков: «List inputs now preserve `#` inside values while still supporting
full-line `#` comments» (PR [#607](https://github.com/docker/metadata-action/pull/607)).

- *Затронуты ли мы:* **нет**. Блок `tags:` состоит из строк
  `type=ref,event=branch`, `type=ref,event=pr`, `type=sha`,
  `type=raw,value=latest,enable={{is_default_branch}}` — символа `#` в них нет.
  Набор распознаваемых типов тегов и логика `{{is_default_branch}}` не менялись,
  поэтому теги `branch` / `pr` / `sha` / `latest` сохранятся.

#### 2.7 `docker/build-push-action` v5 → v7

Пересекаются два мажора.

**v6.0.0 (2024-06-17) — BUILD SUMMARY.** Появились экспорт build record и
генерация GitHub Actions job summary, отключается через `DOCKER_BUILD_SUMMARY`
([release notes v6.0.0](https://github.com/docker/build-push-action/releases/tag/v6.0.0)).

- *Затронуты ли мы:* нет. Это чистое добавление вывода; на результат сборки не
  влияет. Явно гасить его не будем — это была бы лишняя правка вне FR-1.
  Побочный эффект: в PR появится дополнительная секция «Build summary» в
  логе/сводке проверки.

**v7.0.0 (2026-03-05) — Node 24 по умолчанию** (runner ≥ v2.327.1), миграция на
ESM, удаление deprecated env-переменных `DOCKER_BUILD_NO_SUMMARY` и
`DOCKER_BUILD_EXPORT_RETENTION_DAYS`, удаление legacy-поддержки `export-build`
для build summary.

**Изменений в кешировании между v5 и v7 нет.** Проверено: в `action.yml` v7
нет ни одного входа с `required: true`, а `cache-from` / `cache-to` —
сквозные `List`-параметры, передаваемые в `buildx build` без трансформации. Наши
`cache-from: type=gha` и `cache-to: type=gha,mode=max` остаются валидными и
поведение не меняется.

**Изменений в поведении `push` между v5 и v7 нет.** Вход `push` по-прежнему
имеет дефолт `false` и перекрывается нашим явным
`push: ${{ github.event_name != 'pull_request' }}`. Малые изменения v6.19.0–v6.19.2
(скоупинг `GIT_AUTH_TOKEN` к `github.com`) касаются только git-контекста, а мы
используем `context:` из локального каталога.

Также стоит учитывать: с 2026-09-10 доступен параметр `cache-mode` в workflow и
job ([changelog 2026-09-10](https://github.blog/changelog/2026-09-10-control-github-actions-cache-access-with-cache-mode/)).
Workflows, которые его не задают, продолжают использовать существующие
безопасные значения по умолчанию, поэтому наш `type=gha` работает как раньше.

### 3. CodeQL v1 → v4

Это единственный экшен, где между текущей и целевой версией три мажора и где
есть неочевидные требования. Все ответы ниже проверены по исходникам и
официальной документации, а не по памяти.

**Источники.** Официальная инструкция миграции — «Upcoming deprecation of
CodeQL Action v3» ([changelog 2025-10-28](https://github.blog/changelog/2025-10-28-upcoming-deprecation-of-codeql-action-v3/)):
v4 выпущен **2025-10-07**, работает на Node 24, а раздел «Exactly what do I need
to change?» сводит миграцию к замене тегов `@v3` → `@v4` без других правок.
v3 устаревает в декабре 2026. v1 — это Node 12, мёртвый рантайм.

Требования к рантайму по мажорам (прочитано из `action.yml`): v1 → `node12`,
v2 → `node16`, v3 → `node20`, v4 → `node24` (composite actions, `using: node24`).
Все v1–v3 рантаймы удалены, поэтому **v4 — единственная рабочая версия**.

#### 3.1 Обязательно ли `permissions: security-events: write`?

**Не обязательно, и в основном изменении его не добавляем.** Обоснование:

- v4 требует ровно те же права, что и v1: `security-events: write` для
  `analyze` (в README v4 это сказано буквально: «All advanced setup code
  scanning workflows must have the `security-events: write` permission»).
  Смена мажора **не добавляет** нового требования, поэтому FR-2 («добавляем
  право, только если новая версия его требует») не срабатывает.
- Репозиторий — публичный (`drypa/ReceiptCollector`, `visibility: public`), и
  для публичных репозиториев `contents: read` в `permissions` добавлять не нужно.
- Репозиторий **не в явном whitelist**: workflow работает с токеном по умолчанию.

**Ловушка, которую надо зафиксировать.** Если всё-таки добавлять `permissions`,
то добавлять **только** `security-events: write` нельзя: блок `permissions` на
уровне job обнуляет все остальные права в `none`, и `actions/checkout` упадёт с
`Resource not accessible by integration`, потому что потеряет `contents: read`.
Официальный шаблон GitHub поэтому перечисляет несколько прав
([starter-workflow `codeql.yml`](https://github.com/actions/starter-workflows/blob/main/code-scanning/codeql.yml)):
`security-events: write`, `packages: read`, `actions: read`, `contents: read`.

**Решение:** блок `permissions` в основном изменении не добавляется. В плане
реализации зафиксирована готовая контингенция на случай симптома
«Resource not accessible by integration» — с корректным двухстрочным вариантом
и объяснением, почему одной строки мало.

#### 3.2 Нужен ли ещё `git checkout HEAD^2`?

**Оставляем.** Решение противоположно ADR-017, и вот почему.

- Формально шаг не нужен: в актуальном официальном шаблоне GitHub
  (`actions/starter-workflows`, `code-scanning/codeql.yml`) этого шага **нет**,
  как и `fetch-depth: 2`. То есть workaround действительно устарел.
- Но его удаление — это **изменение логики пайплайна** (удаление шага), которое
  FR-1 запрещает, а раздел «Вне объёма» запрещает явно. Смена версий экшена сама
  по себе не требует его удаления: и `fetch-depth: 2`, и `git checkout HEAD^2`
  остаются валидными в v4, а CodeQL не проверяет, detached ли HEAD и на каком
  коммите мы находимся.
- Практически шаг безвреден и даже полезен: он заставляет анализировать head
  ветки PR, а не merge-коммит, что даёт стабильные категории SARIF и не
  привязывает результат к merge-коммиту, который меняется при каждом
  обновлении ветки.
- Формальный риск удаления ненулевой: смена поведения `analyze` при
  анализе merge-коммита вместо head может привести к «stale tips» — старые
  алерты, которые не закрываются. Ради этого выигрыша FR-4 не тратится.

Вывод: шаг остаётся, но ADR-017 в части «удалить workaround» для v4
**не подтверждается** — это отдельное решение, выходящее за объём. Уборка
workaround и переход на современную матрицу `build-mode` оформляются отдельной
задачей (см. «Связанные решения»).

#### 3.3 Работает ли `autobuild` в v4 с Go и JavaScript так же, как в v1?

**Да, и это проверено по исходникам**, потому что для нашего маппинга это главный
риск: матрица `language: ['go', 'javascript']` одна на оба языка, а шаг
`Autobuild` общий.

`src/autobuild.ts`, функция `determineAutobuildLanguages`:

- для `javascript` (интерпретируемый, не traced) отфильтровывает все языки,
  возвращает `undefined`, пишет в лог «None of the languages in this project
  require extra build steps» и **завершает шаг с кодом 0**. Реального падения
  не будет.
- для `go` запускает Go-автобилдер. Более того, в v4 в коде есть явный
  комментарий: «This special case behavior should be removed as part of the next
  major version of the CodeQL Action» — то есть обратная совместимость
  мультиязычного autobuild в v4 **ещё сохранена**.

`github/codeql-action/autobuild@v4` также не удалён: он по-прежнему доступен,
в README значится как «Only used for analyzing languages that require a build»
с рекомендацией использовать `build-mode: autobuild` у `init`. Официальный
шаблон действительно перешёл на `build-mode: ${{ matrix.build-mode }}`, но это
рекомендация, а не требование.

- Идентификаторы `go` и `javascript` в v4 валидны: `javascript` — документированный
  псевдоним для `javascript-typescript`, и анализ TypeScript при этом не
  исключается.

#### 3.4 Есть ли обязательные новые входные параметры?

**Нет.** Проверено чтением `action.yml` всех трёх экшенов v4: единственный вход с
`required: true` — внутренний `analysis-kinds` у `init` со значением по
умолчанию `code-scanning`, который пользователю задавать не нужно. Наш
`languages: ${{ matrix.language }}` остаётся в силе; закомментированный `queries`
по-прежнему закомментирован.

Дополнительно в v4 есть проверки, которые на github.com безвредны:
`checkGitHubVersionInRange` выходит сразу для варианта DOTCOM, а
`checkActionVersion` предупреждает только версии `< 4`. Поскольку мы переходим
на v4, предупреждение «CodeQL Action v3 will be deprecated in December 2026» в
логах **не появится** — это напрямую закрывает критерий приёмки «нет
предупреждений о deprecated actions».

### 4. Требования к раннеру

**`ubuntu-latest` достаточно. Изменять `runs-on` не требуется, и это не выходит за
рамки FR-1.**

- Все целевые версии требуют **Actions Runner ≥ v2.327.1**
  (checkout v5, setup-node v5, setup-dotnet v5, все четыре `docker/*` v4/v6/v7,
  CodeQL v4 через Node 24). Это требование к *версии раннера*, а не к образу.
- GitHub-hosted раннеры обновляются до последней версии автоматически, поэтому
  на `ubuntu-latest` (и на текущем Ubuntu 24.04, и на Ubuntu 26.04 после
  миграции) оно выполнено.
- Для CodeQL отдельно проверено: официальный шаблон задаёт
  `runs-on: ubuntu-latest` (с единственным исключением `macos-latest` для
  Swift). Требований к более новому образу у v4 нет — в исходниках отсутствует
  любая проверка минимальной версии раннера или дистрибутива.
- Экшены `docker/*` требуют работающего Docker-демона. Он есть на всех
  GitHub-hosted Linux-образах, и `docker/setup-buildx-action@v4` сам поднимает
  нужный builder.

**Явные исключения за рамками FR-1 в этом ADR — их нет.** В частности,
`runs-on` не меняется: ни `ubuntu-24.04` (чтобы удержаться от миграции), ни
`ubuntu-26.04` (чтобы тестироваться заранее). Обе меры — самостоятельные
изменения пайплайна, и заказчик их не заказывал.

### 5. Ограничение правила отката (FR-4)

Это самое важное ограничение, и его нужно назвать прямо, иначе правило отката
неисполнимо.

Проверено чтением `runs.using` в `action.yml` **всех** версий каждого экшена:

| Экшен | Цель | Есть ли рабочий откат | Почему |
|---|---|---|---|
| `actions/checkout` | v7 | ✅ v6, v5 | v5/v6/v7 — все `node24` |
| `actions/setup-node` | v7 | ✅ v6, v5 | v5/v6/v7 — все `node24` |
| `actions/setup-dotnet` | v6 | ✅ v5 | v5/v6 — оба `node24` |
| `docker/setup-buildx-action` | v4 | ❌ | v3 — `node20`, удалён |
| `docker/login-action` | v4 | ❌ | v3 — `node20`, удалён |
| `docker/metadata-action` | v6 | ❌ | v5 — `node20`, удалён |
| `docker/build-push-action` | v7 | ❌ | v5 и v6 — `node20`, удалены |
| `github/codeql-action/*` | v4 | ❌ | v1 `node12`, v2 `node16`, v3 `node20` |

**Для 5 из 9 позиций отката не существует.** Возврат `docker/build-push-action`
на `@v5` не «возвращает рабочее состояние» — он возвращает заведомо
нерабочее. Это прямое следствие того, что Node 20 удалён, и ранее
не существовавшее ограничение.

Практическое определение отката для таких позиций:

- **Куда есть лестница** (checkout, setup-node, setup-dotnet): откатываем на
  следующий более старый мажор, который всё ещё работает на Node 24. Это
  исполнимый FR-4.
- **Куда лестницы нет** (все `docker/*`, CodeQL): откат конкретного экшена
  технически невозможен. Единственные доступные рычаги:
  1. чинить вперёд (правильный путь, откат не восстановит зелёный статус);
  2. откатить workflow целиком до предыдущего коммита — что вернёт его в
     заведомо сломанное состояние и оставит `master` без работающего CI;
  3. остановить публикацию образа, оставив сборку — то есть изменить логику
     пайплайна, что запрещено.

  Выбирается (1) как основной вариант. Если (1) невозможен в разумные сроки —
  задача закрывается частично по FR-3, а в репозитории заводится follow-up, и
  это фиксируется явно, а не молча.

**Практический смысл правила FR-4 сейчас** — не «вернуть старый тег», а
«изолировать виновника и сузить периметр»: коммит на файл → `git revert`
одного коммита сужает ровно один workflow, а сужение до конкретного экшена
возможно только для трёх позиций из девяти.

### 6. Стратегия выкатки

**Один коммит на каждый из трёх workflow-файлов, три последовательных PR — не
один коммит на всё.**

Почему не один коммит:

- Три workflow дают три независимых статусных проверки. В одном PR они
  запускаются параллельно, и одна красная проверка маскирует зелёный статус
  двух других. При последовательных PR зелёный статус PR-1 виден, пока
  чинят PR-2.
- `checkout` — первый шаг во всех трёх workflow. Если несовместим именно он,
  падают все три одинаково, и по одному PR нельзя отличить «сломался checkout»
  от «сломался checkout в другом файле». Разделение по файлам убирает
  неоднозначность.
- `codeql-analysis.yml` устроен принципиально иначе (матрица, `autobuild`,
  загрузка SARIF, права). Смешивать его с Docker-экшенами в одном PR — значит
  при поломке CodeQL грешить на `docker/*`.
- `git revert` одного коммита = точечный откат ровно одного workflow. С
  тремя workflow в одном коммите частичный откат невозможен.

**Порядок — от наименьшего радиуса поражения к наибольшему:**

1. **PR-1: `build-backend-image.yml`.** Самый простой: `checkout` + четыре
   `docker/*`, без Node, без .NET, без тестов приложения. Проверяет 7 замен,
   включая реальный `push` в DockerHub, формирование тегов и
   `cache-to: type=gha,mode=max`. Всё это нужно, чтобы доверять тем же
   заменам в следующих шагах.
2. **PR-2: `codeql-analysis.yml`.** Изолирует риск CodeQL от Docker. Отдельно
   проверяет `checkout@v7` в другой конфигурации (с `fetch-depth: 2` и
   `HEAD^2`), `autobuild@v4` для Go и no-op для JavaScript, загрузку SARIF и
   права. Порядок до analytics выбран потому, что у этого workflow есть
   `on.pull_request` без `paths`-фильтра — он запускается на каждом PR и
   даёт самый быстрый и самый чистый сигнал. Побочный эффект порядка:
   к PR-3 CodeQL уже зелёный, поэтому красная analytics-проверка однозначно
   указывает на свой workflow.
3. **PR-3: `build-analytics-image.yml`.** Последним, потому что у него
   наибольший радиус поражения: единственный workflow, который ставит Node и
   .NET и выполняет `npm run lint` и `dotnet test`. К этому моменту
   `checkout@v7` и все четыре `docker/*` уже доказаны в PR-1, а
   `actions/checkout@v7` — ещё и в PR-2. Непроверенными остаются только
   `setup-node@v7` и `setup-dotnet@v6`, то есть риск сужен до двух экшенов.

**Как проверять в браузере, не имея возможности откатить уже запущенный CI.**
Ключевой приём: у этого репозитория `on.push` у обоих build-workflow **не
ограничен** ветками и включает путь к самому workflow-файлу, поэтому
**обычный push ветки запускает их**. Это и есть транспорт проверки:

| Что проверяем | Чем запускается | Где смотреть |
|---|---|---|
| `build-backend-image.yml` (PR-1) | push ветки **и** PR | Actions → Docker Image CI |
| `codeql-analysis.yml` (PR-2) | только PR (`on.push` ограничен `master`) | Actions → CodeQL, затем Security → Code scanning |
| `build-analytics-image.yml` (PR-3) | push ветки | Actions → Build Analytics Docker Image |

Известная асимметрия, зафиксированная как ограничение проверки: у
`build-analytics-image.yml` в `on.pull_request.paths` указан только
`'Analytics/**'`, тогда как в `on.push.paths` добавлен ещё и сам workflow-файл.
Поэтому PR, меняющий только версии в
`.github/workflows/build-analytics-image.yml`, **не запустит эту проверку в
PR вообще** — она сработает только на push. Обойти это, не меняя триггеры
(запрещено FR-1) и не трогая код приложения (запрещено «Вне объёма»), нельзя.
Компенсирующие меры: все четыре её экшена, кроме `setup-node` и
`setup-dotnet`, уже проверены в PR-1, а для оставшихся двух подготовлена
готовая строка отката. Выравнивание `paths`-фильтров выносится в отдельную
задачу.

Побочный эффект проверки через push ветки, о котором нужно знать заранее: при
`github.event_name == 'push'` условия `if: github.event_name != 'pull_request'`
не срабатывают, поэтому сборка **реально запушит** образы. В DockerHub появятся
теги по имени ветки и по короткому SHA. Это существующее поведение
(оно воспроизводится при любом push ветки и сегодня), а не следствие наших
правок, но дежурному по релизу стоит знать: `latest` при этом не ставится,
потому что `enable={{is_default_branch}}` на ветке ложно.

### 7. Суперсессия документов

Задача `update-github-actions.md` заменяет тройку
`docs/tasks/update-codeql-actions-v3.md` + ADR-017 +
`docs/plans/update-codeql-actions-v3.md`. Решение по каждому артефакту:

| Артефакт | Действие | Почему |
|---|---|---|
| `docs/adr/017-upgrade-codeql-actions-v3.md` | **сохраняется, статус → «Заменено»** | По конвенции репозитория ADR не удаляются: это зафиксированное историческое решение. Но оно частично неверно для v4 (пункты 3 и 4 в нём больше не подтверждаются) — это обязано быть видно, иначе следующий читатель выполнит устаревшую рекомендацию. В статус добавляется явная ссылка на ADR-025 с указанием, какие именно выводы ADR-017 отменены. |
| `docs/plans/update-codeql-actions-v3.md` | **удаляется** | План — исполняемый рабочий документ, а не запись о решении. План на v3 нельзя выполнить: v3 работает на Node 20 и мёртв. Хранить план, который невозможно выполнить, — источник ошибки. Заменяется этим документом. |
| `docs/tasks/update-codeql-actions-v3.md` | **помечается заменённым, остаётся в `docs/tasks/`** | Задача помечена в статусе и в «Техническом решении» ссылкой на ADR-025. По конвенции черновики задач не удаляются; статус «Draft» с 2026-09-06 при отсутствии 9 недель активности делает его источником неверных ожиданий, но не блокирует работу. Физически удалять заведённую задачу без отдельного решения заказчика — избыточно. |
| `docs/tasks/update-github-actions.md` | **остаётся, дополняется ссылкой на ADR-025** | Это активная задача. |

Отдельная задача на CodeQL, как и решил заказчик, не заводится.

## Компромиссы

| Решение | Плюсы | Минусы | Почему выбрано |
|---|---|---|---|
| Заменить только теги, структуру не трогать | Минимальный diff, FR-1 соблюдён, откат = `git revert` коммита | Устаревший `HEAD^2` и legacy-матрица остаются | Удаление шага — запрещённое изменение логики пайплайна; выигрыш не окупает риск «stale tips» |
| Не добавлять `permissions` | Нет риска обнулить `contents: read` и сломать checkout | Не hardening; при жёстких настройках токена понадобится отдельная правка | v4 не добавляет требований; контингенция описана в плане |
| Три коммита / три PR | Точная атрибуция поломки, частичный откат, зелёный master после каждого шага | Три слияния, дольше до полного результата | Единственный способ выполнить FR-4 при отсутствии зелёной базы |
| Порядок backend → codeql → analytics | Каждый шаг сужает множество непроверенных экшенов | CodeQL чинится раньше analytics | Сначала дешёвая проверка инфраструктурных замен, потом рискованные |
| Оставить `docker/*` без отката | Нет мёртвых опций в workflow | Поломка `docker/*` чинится только вперёд | Объективное ограничение: все предыдущие мажоры мертвы по рантайму |
| Не гасить Build Summary | Нет лишних правок | Лишняя секция в сводке PR | Укладывается в edge case задачи «вывод логов меняется, на результат не влияет» |

## Нефункциональные аспекты

- **Безопасность.** `checkout@v7` добавляет защиту от «pwn request» для
  `pull_request_target`/`workflow_run` (наш случай не затрагивает, но
  уязвимость в коде устранена). `checkout@v6` убирает токен из `.git/config` в
  отдельный файл — снижает риск утечки через чтение конфига в логах. CodeQL
  обновляется до актуальных query suites.
- **Поддерживаемость.** Все девять позиций переходят на мажоры, которые
  GitHub будет обновлять; CodeQL v3 устаревает в декабре 2026, то есть мы
  уходим из-под этого дедлайна с запасом.
- **Производительность.** Прямых изменений нет. `docker/build-push-action`
  начнёт писать build summary (дополнительная секция в логе). Кеш GHA работает
  как раньше. `setup-node` не включит автокеш — проверено по исходнику.
- **Наблюдаемость.** Критерий «нет предупреждений о deprecated actions»
  достигается автоматически: `checkActionVersion` в CodeQL v4 не предупреждает
  для версий `>= 4`, а остальные экшены — `node24`.
- **Стоимость.** Ветка-push проверка публикует тестовые образы с тегами ветки и
  SHA. Стоимость минимальна, поведение существующее.

## Последствия

### Позитивные

- CI восстанавливается: сейчас неработоспособен полностью, после задачи —
  полностью на актуальных мажорах.
- Обновление завершено до миграции `ubuntu-latest` → Ubuntu 26.04
  (19.10–19.11.2026), поэтому два риска не смешиваются.
- Появляется частичный откат хотя бы для `checkout`, `setup-node`,
  `setup-dotnet`.

### Отрицательные / принятые риски

- Для пяти из девяти позиций отката нет (см. раздел 5) — риск чинится вперёд.
- `build-analytics-image.yml` не проверяется в PR из-за асимметрии
  `on.pull_request.paths` и `on.push.paths`; проверяется на push ветки.
- Push ветки публикует тестовые образы в DockerHub (существующее поведение).
- В сводке PR появится секция Build Summary.

## Отклонённые альтернативы

| Альтернатива | Почему отклонена |
|---|---|
| Один коммит на все три workflow | Не даёт атрибуции поломки; делает частичный откат невозможным |
| Шаг `git checkout HEAD^2` удалить | Запрещено FR-1 и «Вне объёма»; шаг валиден в v4, риск «stale tips» при смене модели анализа |
| `fetch-depth: 2` убрать | Изменение логики; в v4 не требуется, но и не мешает |
| Заменить `autobuild` на `build-mode:` в `init` | Перестройка матрицы, меняет категории SARIF, запрещено FR-1 |
| Добавить `permissions` превентивно | v4 не добавляет требований; блок обнуляет остальные права и может сломать checkout |
| Пиннить на SHA для безопасности | Прямо запрещено «Вне объёма» |
| Зафиксировать `runs-on: ubuntu-24.04` | Запрещено FR-1, лишает дежурного автопатчей ОС; отдельная задача вместо этого |
| Включить `actionlint` / `zizmor` | Прямо запрещено «Вне объёма» |
| Обновить Node/.NET/Go в образах | Прямо запрещено «Вне объёма» |

## Связанные решения

- [ADR-017](017-upgrade-codeql-actions-v3.md) — заменено этим ADR (частично).
- Последующие задачи, порождаемые настоящим ADR:
  - подготовка к миграции `ubuntu-latest` → Ubuntu 26.04 (октябрь–ноябрь 2026);
  - модернизация `codeql-analysis.yml` (удаление `git checkout HEAD^2` и
    `fetch-depth: 2`, переход на современную матрицу `build-mode`, выравнивание
    идентификаторов языков) — отдельная задача, CodeQL-специфика в рамках
    текущей не выполняется;
  - выравнивание `on.pull_request.paths` и `on.push.paths` в build-workflow,
    чтобы изменение версий в workflow-файле запускало проверку в PR;
  - включение `permissions: security-events: write` + `contents: read` как
    hardening, если захочется уйти от токена по умолчанию.
