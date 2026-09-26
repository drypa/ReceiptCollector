# План: починка ESLint фронтенда и включение линтинга в CI

**Связанный ADR:** [ADR-024](../adr/024-frontend-eslint-flat-config.md)
**Файл задачи:** [fix-frontend-eslint.md](../tasks/fix-frontend-eslint.md)

## Диагностика: подтверждённые факты

Все утверждения диагностики проверены локально и **подтверждены**:

| Факт | Проверка | Результат |
|------|----------|-----------|
| `eslint.config.js:15` ломает линтер | `npm run lint` | `exit 2`, сообщение «`plugins` defined as an array of strings», файл не анализируется вовсе |
| `reactHooks.configs['recommended-latest']` — legacy-форма | инспекция объекта | `{ plugins: ['react-hooks'], rules: {...} }`, 17 правил |
| `reactHooks.configs.flat['recommended-latest']` существует | инспекция объекта | `{ plugins: { 'react-hooks': {...} }, rules: {...} }` |
| `reactHooks.configs.flat.recommended` существует | инспекция объекта | 16 правил; отличие от `flat['recommended-latest']` ровно одно — нет `react-hooks/void-use-memo` |
| `reactRefresh.configs.vite` уже flat-совместим | инспекция объекта | `plugins` — объект, правки не нужны |
| С flat-пресетом 0 ошибок / 0 предупреждений | прогон по `.` | `exit 0`, 35 файлов, 0 сообщений; с `--max-warnings 0` — тоже `exit 0` |
| `npm ci` не падает на рассинхроне lock | `npm ci --dry-run` | `exit 0` |
| Установочные скрипты в графе зависимостей | `grep hasInstallScript` | ровно 1 пакет — `fsevents` (optional, macOS-only); в `package.json` нет `pre/postinstall`/`prepare` |
| В `package-lock.json` не фиксируется `scripts` | инспекция `packages[""]` | ключи: `name`, `version`, `dependencies`, `devDependencies` |
| `npm run build` сейчас зелёный | прогон | `exit 0` (tsc + vite) |

**Дополнительно установлено (важно для FR-3):**

- ESLint **игнорирует `--config` в режиме `--stdin`** и всегда подхватывает
  `eslint.config.js` из текущего каталога. Проверено: с исправленным конфигом
  `--stdin` отрабатывает, со сломанным — падает `exit 2`. Это делает `--stdin`
  идеальным каналом для контрольной проверки: она проверяет именно коммитируемый
  конфиг.
- Конфиг объявляет `files: ['**/*.{ts,tsx}']`, поэтому `.js`/`.mjs`-файлы
  (включая сам `eslint.config.js`) обходятся ESLint, но **ни одним правилом не
  покрыты**. Проверено намеренным `no-unused-vars`: в `.js` — `exit 0` без
  сообщений, в `.ts` — ошибка. Следствия: (а) edge case «`eslint .` проходит по
  `eslint.config.js`» безопасен и новых нарушений дать не может; (б) контрольная
  проба обязана иметь расширение `.ts`/`.tsx`.
- `tsconfig.app.json` содержит `"include": ["src"]` и `noUnusedLocals: true`.
  Временный `.tsx`, оставленный в `src/`, сломал бы `npm run build` (`tsc -b`).
  Ещё один аргумент против создания файлов в репозитории для FR-3.

## Файлы к изменению

Полный, исчерпывающий список — **три файла**:

1. `Analytics/frontend/eslint.config.js` — одна строка (R1)
2. `Analytics/frontend/package.json` — только значение скрипта `lint` (R3)
3. `.github/workflows/build-analytics-image.yml` — вставка двух шагов (FR-5)

Плюс два документа, создаваемых этой задачей:
`docs/adr/024-frontend-eslint-flat-config.md`, `docs/plans/fix-frontend-eslint.md`.

> В рабочем дереве на момент планирования есть **посторонние** незакоммиченные
> правки `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
> `appsettings.Development.json`. Они не относятся к задаче; все проверки ниже
> используют путь-скоупы вида `git status --porcelain -- <путь>`, чтобы эти
> правки не маскировали и не имитировали результат.

---

## Шаг 1. `Analytics/frontend/eslint.config.js`

**Правка ровно одной строки (строка 15).**

```diff
     extends: [
       js.configs.recommended,
       tseslint.configs.recommended,
-      reactHooks.configs['recommended-latest'],
+      reactHooks.configs.flat['recommended-latest'],
       reactRefresh.configs.vite,
     ],
```

Ничего больше в файле не меняется. В частности, сохраняются:
- `globalIgnores(['dist'])`;
- блок `rules: { 'react-hooks/set-state-in-effect': 'off' }` вместе с
  поясняющим комментарием — он указывает на осознанное техническое решение
  отложить рефакторинг хуков, и его удаление вскроет массовые нарушения,
  не относящиеся к этой задаче;
- `reactRefresh.configs.vite` — уже flat-совместим.

**Проверка шага:**

```bash
cd Analytics/frontend
git diff -- eslint.config.js     # ожидается ровно 1 строка +/- , замена .flat
npx eslint .                     # ожидается exit 0, пустой вывод
```

## Шаг 2. `Analytics/frontend/package.json`

**Правка только значения скрипта `lint` (строка 9).**

```diff
     "scripts": {
       "dev": "vite --host",
       "build": "tsc -b && vite build",
-      "lint": "eslint .",
+      "lint": "eslint . --max-warnings 0",
       "preview": "vite preview"
     },
```

Ключи `dependencies` и `devDependencies` не трогаются. Секция `scripts` в
lockfile не фиксируется, поэтому `package-lock.json` рассинхронизироваться не
может (см. R2 в ADR-024).

**Проверка шага:**

```bash
cd Analytics/frontend
git diff -- package.json         # ожидается 1 строка +/- в scripts.lint
git status --porcelain -- package-lock.json   # ожидается пусто
npm run lint                     # ожидается exit 0, пустой вывод
```

## Шаг 3. `.github/workflows/build-analytics-image.yml`

**Вставка двух шагов после `- uses: actions/checkout@v4` (строка 16) и до
`- name: Setup .NET` (строка 18).**

```diff
     steps:
     - uses: actions/checkout@v4

+    - name: Setup Node
+      uses: actions/setup-node@v4
+      with:
+        node-version: '22'
+
+    - name: Install frontend dependencies
+      working-directory: Analytics/frontend
+      run: npm ci --no-audit --no-fund
+
+    - name: Lint frontend
+      working-directory: Analytics/frontend
+      run: npm run lint
+
     - name: Setup .NET
       uses: actions/setup-dotnet@v4
       with:
         dotnet-version: '10.0.x'
```

**Почему именно так:**

- **Версия Node фиксируется явно** (`node-version: '22'`), а не «latest» —
  прямое закрытие edge case «расхождение локальной версии Node и CI-версии».
  Значение `22` берётся из `Analytics/Dockerfile:2` (`node:22-alpine`), то есть
  CI-линтинг и Docker-сборка работают на одной major-версии.
- **`actions/setup-node@v4`**, а не `@v7` (актуальный upstream на сентябрь 2026):
  в этом же workflow соседние `actions/*` закреплены на `@v4` (`checkout@v4`,
  `setup-dotnet@v4`). Внутри одного workflow смешивать majors одного вендора —
  источник неочевидной разницы в поведении; принцип согласованности action-версий
  в пределах workflow уже зафиксирован в ADR-017. Если команда решит поднять все
  `actions/*` разом — это отдельная задача по обновлению зависимостей.
- **Два отдельных шага, а не один.** Критерий приёмки требует, чтобы при
  нарушении PR «падал именно на нём» — отдельный именованный шаг `Lint frontend`
  даёт эту атрибуцию в UI. Смешивание `npm ci && npm run lint` в один `run`
  скрыло бы, упала ли установка или линтинг.
- **`npm ci` без кеширования** (`cache: 'npm'` не добавляется): в корне репозитория
  нет lockfile, поэтому потребовался бы `cache-dependency-path:
  Analytics/frontend/package-lock.json` — лишняя точка отказа. Кеш — оптимизация,
  не входящая в объём задачи; при желании выносится отдельным пунктом.
- **`npm run build` не дублируется** — он уже выполняется стадией `frontend` в
  `Analytics/Dockerfile:7` внутри шага Docker-сборки этого же workflow.

**Почему `--no-audit --no-fund`** — см. раздел «Безопасность `npm ci`».

**Проверка шага:**

```bash
git diff -- .github/workflows/build-analytics-image.yml
# Блок on: (строки 3–10) в diff не должен фигурировать вообще.
python3 -c "import yaml,sys; d=yaml.safe_load(open('.github/workflows/build-analytics-image.yml')); print([s.get('name') or s.get('uses') for s in d['jobs']['build']['steps']])"
# Ожидаемый порядок:
#   actions/checkout@v4
#   Setup Node
#   Install frontend dependencies
#   Lint frontend
#   Setup .NET
#   Start Docker containers for tests
#   Run tests
#   ...
```

---

## Шаг 4. Контрольная проверка FR-3 (без мусора и без коммита)

### Выбранный способ: `eslint --stdin --stdin-filename`

Контрольная проверка выполняется **после** шагов 1–2, то есть на исправленном
`eslint.config.js`, и **ничего не пишет в репозиторий**:

```bash
cd Analytics/frontend

PROBE_DIR=$(mktemp -d)          # каталог ВНЕ репозитория

# --- Позитивный контроль: нарушения по одному на каждое семейство правил ---
cat > "$PROBE_DIR/probe.tsx" <<'EOF'
import { useState } from 'react'

export function probeHelper() {
  const bad: any = 1
  return bad
}

export function ProbeComponent({ flag }: { flag: boolean }) {
  if (flag) {
    const [n] = useState(1)
    console.log(n)
  }
  return <div>{probeHelper()}</div>
}
EOF

npx eslint --stdin --stdin-filename src/__lint_probe__.tsx --max-warnings 0 \
  < "$PROBE_DIR/probe.tsx"
echo "exit=$?"      # ОЖИДАЕТСЯ 1
```

Ожидаемый вывод — три ошибки, exit 1 (проверено на исправленном конфиге):

```
   3:17  error  Fast refresh only works when a file only exports components…  react-refresh/only-export-components
   4:14  error  Unexpected any. Specify a different type                        @typescript-eslint/no-explicit-any
  10:17  error  React Hook "useState" is called conditionally…                   react-hooks/rules-of-hooks
✖ 3 problems (3 errors, 0 warnings)
```

```bash
# --- Негативный контроль: та же обвязка, заведомо чистый файл ---
cat > "$PROBE_DIR/clean.tsx" <<'EOF'
import { useState } from 'react'

export function CleanComponent() {
  const [n] = useState(0)
  return <div>{n}</div>
}
EOF

npx eslint --stdin --stdin-filename src/__lint_probe__.tsx --max-warnings 0 \
  < "$PROBE_DIR/clean.tsx"
echo "exit=$?"      # ОЖИДАЕТСЯ 0

rm -rf "$PROBE_DIR"

# --- Доказательство отсутствия мусора ---
cd /home/drypa/projects/ReceiptCollector
git status --porcelain
# Ожидаются только: appsettings.json, appsettings.Development.json
# и три файла этой задачи. Никаких __lint_probe__, probe.*, *.tmp.
```

### Почему этот способ безопасен

| Свойство | Как обеспечено |
|----------|----------------|
| Не требует коммита | Проба не меняет ни одного отслеживаемого файла; в git попадает только сам фикс |
| Не оставляет мусора | Файлы создаются в `$(mktemp -d)` вне репозитория и удаляются; в дерево не пишется ничего |
| Проверяет **настоящий** конфиг | ESLint игнорирует `--config` при `--stdin` и всегда берёт `eslint.config.js` из cwd — подставленная копия исключена в принципе |
| Проверяет все три семейства | Проба намеренно нарушает по одному правилу из `react-hooks`, `react-refresh`, `typescript-eslint` |
| Исключает ложноотрицательный результат | Негативный контроль: та же обвязка на чистом файле обязана дать `exit 0` |
| Не ломает `npm run build` | Файла в `src/` не появляется, значит `tsc -b` его не подхватывает |

### Отвергнутые альтернативы

| Способ | Почему не используется |
|--------|------------------------|
| Временный файл в `Analytics/frontend/src/` | Попадёт в `tsc -b` (`include: ["src"]`, `noUnusedLocals: true`) и может сломать `npm run build`; есть риск забыть удалить и закоммитить пробу |
| Правка существующего файла в `src/` + `git checkout --` | Оставляет мусор при прерывании; затрагивает отслеживаемый файл, повышает риск случайного коммита |
| `git stash` / временный коммит | Реквизит, прямо запрещённый FR-3 («не требовало коммита»), и риск потерять stash |
| Временный конфиг + `--config` | В режиме `--stdin` флаг игнорируется; в обычном режиме проверялся бы не коммитируемый конфиг |

---

## Проверка безопасности `npm ci` в CI-шаге

| Риск | Оценка | Обоснование |
|------|--------|-------------|
| Рассинхрон lock и `package.json` | **Отсутствует** | `npm ci --dry-run` → `exit 0`. Правка затрагивает только `scripts`, который в lockfile не записывается |
| `postinstall`/`prepare` в корневом пакете | **Отсутствует** | В `package.json` нет таких скриптов |
| Установочные скрипты в дереве зависимостей | **Отсутствует** | Ровно один пакет с `hasInstallScript` — `fsevents` (optional, macOS-only); на `ubuntu-latest` не устанавливается |
| Аудит уязвимостей | **Невлияющий на exit code** | `npm audit` не провалит установку, но требует сетевого запроса к audit-эндпоинту и печатает шум. Флаг `--no-audit` делает шаг детерминированным и быстрее |
| Модификация `package-lock.json` в CI | **Отсутствует** | `npm ci` по спецификации не пишет lockfile (в отличие от `npm install`); рабочее дерево в CI всё равно disposable |
| Расхождение с Docker-сборкой | **Отсутствует** | `Analytics/Dockerfile:5` выполняет тот же `npm ci` на том же lock и на `node:22-alpine`; расхождение флагов `--no-audit/--no-fund` на состав дерева не влияет |
| Платформенные бинарники (rolldown/lightningcss) | **Отсутствует** | В lock есть `linux-x64` записи; `npm ci --dry-run` на текущей машине их разрешил |

**Вывод:** `npm ci` безопасен. Флаги `--no-audit --no-fund` — устранение шума и
сетевой зависимости, не влияющее на состав установленного дерева. Допустимо
заменить на голое `npm ci`; риск при этом не меняется.

---

## Совместимость с существующим пайплайном

| Аспект | Оценка |
|--------|--------|
| Порядок шагов | Новые шаги (16–28) идут после `checkout` и до `Setup .NET`, до `Start Docker containers for tests`, до `Run tests`. Node-шаг не зависит от postgres/mongo — `npm ci` и `npm run lint` не обращаются к сети сервисов и не требуют БД. Требование FR-5 выполнено |
| Взаимодействие с `if: github.event_name != 'pull_request'` | **Конфликта нет.** Условие стоит только на шаге `Login to DockerHub` (строка 47) и на `push:` в `docker/build-push-action` (строка 68). Новые шаги условий не имеют и выполняются на обоих событиях — то есть линтинг действительно является гейтом для PR, а не только для push. Ни одно из условий не является `always()`, поэтому новый шаг не «пропускает» последующие и не «залипает» на них |
| Поведение при провале Node-шага | Job `build` падает целиком, .NET-тесты и Docker-сборка не запускаются. Это **желаемое** поведение: нечего собирать и тестировать код, не прошедший линтер. Побочный эффект — при провале линтинга PR не получит и Docker-образа; для PR это нерелевантно (`push: ${{ github.event_name != 'pull_request' }}` и логин в DockerHub на PR и так отключены) |
| Асимметрия `dotnet test` / линтинга | `dotnet test` выполняется с `--configuration Release` (dotnet-шаг остаётся без изменений). Линтинг не имеет аналогичного «режима сборки» — он конфигурационно независим, расхождение невозможно |
| Триггеры | Блок `on:` не изменяется. Фронтенд уже подпадает под `paths: 'Analytics/**'` и в `push`, и в `pull_request` |
| Новый workflow | Не создаётся (FR-5) |
| `actions/setup-node@v4` на ubuntu-latest | Официальное action, поддерживаемое; конфликтов с `setup-dotnet@v4` и `docker/*` не имеет — каждый action изолирован в своём окружении |

---

## Порядок выполнения и команды проверки

Шаги выполняются строго последовательно: каждый следующий опирается на
предыдущий, а шаг 4 имеет смысл только после 1–2.

```bash
cd /home/drypa/projects/ReceiptCollector
git status --porcelain      # зафиксировать исходное состояние (2 appsettings + задача)
```

### 1. Починить конфиг

```bash
cd Analytics/frontend
# заменить строку 15: reactHooks.configs['recommended-latest']
#                     →  reactHooks.configs.flat['recommended-latest']
git diff -- eslint.config.js
npx eslint .                # exit 0, пустой вывод
```

### 2. Сделать строгость одинаковой

```bash
# package.json: "lint": "eslint ." → "eslint . --max-warnings 0"
git diff -- package.json
git status --porcelain -- package-lock.json   # пусто
npm run lint                # exit 0, пустой вывод
npm run build               # exit 0 — tsc + vite не должны пострадать
```

### 3. Прогнать контрольную проверку FR-3

```bash
# полный сценарий из шага 4 плана
# ожидается: exit 1 с 3 ошибками, затем exit 0, затем чистый git status
```

### 4. Вставить CI-шаги

```bash
cd /home/drypa/projects/ReceiptCollector
# вставить два шага в build-analytics-image.yml после checkout
git diff -- .github/workflows/build-analytics-image.yml
python3 -c "import yaml;d=yaml.safe_load(open('.github/workflows/build-analytics-image.yml'));print([s.get('name') or s.get('uses') for s in d['jobs']['build']['steps']])"
```

### 5. Итоговая проверка .NET-части (регрессия)

```bash
cd Analytics
dotnet test
```

### 6. Проверка в CI

Открыть PR с изменением в `Analytics/frontend/src` и убедиться, что в статусе
появились зелёные шаги `Setup Node` → `Install frontend dependencies` →
`Lint frontend`. Затем — временный PR с искусственным нарушением
(см. ниже) для подтверждения, что падает именно `Lint frontend`.

**Проверка «падает именно на линте» без коммита в основную ветку:** создать
ветку от текущей, внести в любой файл `Analytics/frontend/src` нарушение
(например, `useState` в условном блоке), запушить, открыть PR, убедиться, что
красным горит `Lint frontend`, затем закрыть PR и удалить ветку.

---

## Чек-лист критериев приёмки

- [ ] `cd Analytics/frontend && npm run lint` → `exit 0`, пустой вывод
- [ ] В `git diff -- eslint.config.js` ровно одна заменённая строка
      (`.configs['recommended-latest']` → `.configs.flat['recommended-latest']`)
- [ ] Блок `rules: { 'react-hooks/set-state-in-effect': 'off' }` и комментарий
      к нему сохранены
- [ ] `git diff -- package.json` — только строка `lint`; `dependencies` /
      `devDependencies` не тронуты
- [ ] `git status --porcelain -- Analytics/frontend/package-lock.json` → пусто
- [ ] `npm run build` → `exit 0`
- [ ] **FR-3:** позитивный контроль → `exit 1` и три ID правил
      (`react-hooks/rules-of-hooks`, `react-refresh/only-export-components`,
      `@typescript-eslint/no-explicit-any`)
- [ ] **FR-3:** негативный контроль → `exit 0`
- [ ] **FR-3:** `git status --porcelain` содержит только ожидаемые файлы —
      никаких `__lint_probe__`, `probe.*`, `*.tmp`, временных конфигов
- [ ] В `build-analytics-image.yml` шаги `Setup Node` /
      `Install frontend dependencies` / `Lint frontend` идут после
      `actions/checkout@v4` и до `Setup .NET`
- [ ] Блок `on:` в diff не фигурирует; новых workflow нет
- [ ] `node-version` зафиксирован явно (`'22'`) и совпадает с `Analytics/Dockerfile:2`
- [ ] Шаги принимаются GitHub Actions (валидный YAML, `working-directory`
      указывает на существующий каталог)
- [ ] В PR виден зелёный шаг `Lint frontend`; при искусственном нарушении падает
      именно он
- [ ] `cd Analytics && dotnet test` → зелёный, 194 теста
- [ ] Предупреждения NU1902/NU1903 остались как были (не «попутно исправлены»)

## Риски и митигация

| Риск | Вероятность | Митигация |
|------|-------------|-----------|
| После включения настоящего пресета всплывут реальные нарушения (FR-6) | Низкая — проверено: 0 ошибок, 0 предупреждений на текущем коде | Исправить в этой же задаче; это явный сценарий FR-6, не блокер |
| `npm ci` в CI упадёт из-за рассинхрона lock | Отсутствует — `npm ci --dry-run` → `exit 0`, и тот же `npm ci` уже работает в `Analytics/Dockerfile` | Откатить шаг `Install frontend dependencies`; `Lint frontend` заведомо не запустится |
| Случайный коммит временных файлов проверки | Отсутствует | Проверка FR-3 не создаёт файлов в репозитории вообще (см. шаг 4) |
| Кто-то снова подключит legacy-пресет | Средняя | Единственная защита — CI-шаг линтинга (падает `exit 2`). Автотест конфига вне объёма по условию задачи |
| `actions/setup-node@v4` устареет относительно `@v7` | Низкая, косметическая | Пиннинг по major соответствует остальным `actions/*` в этом workflow; общий апгрейд actions — отдельная задача |
| Флаг `--max-warnings 0` сделает CI слишком строгим к новым warn-правилам | Средняя | Осознанный компромисс (R3 в ADR-024): «предупреждение = провал» — требование FR-2. Три warn-правила пресета (`exhaustive-deps`, `incompatible-library`, `unsupported-syntax`) — это сигналы о реальных рисках React |
