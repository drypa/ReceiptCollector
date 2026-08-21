# Задача: Управление категориями магазинов (Merchant Category Management)

## 1. Бизнес-ценность / Цель

На данный момент администратор может назначать категории отдельным **товарам (Commodity)** внутри чека, но не может категоризировать **магазины (Merchant)** целиком. Категория магазина уже существует в модели данных (поле `category` в таблице `merchants` и перечисление `MerchantCategory`), однако в интерфейсе администратора нет возможности её просматривать и изменять.

**Цель:** Дать администратору возможность просматривать список всех магазинов и назначать каждому магазину категорию. Это позволит в будущем строить аналитику по категориям магазинов (например, «сколько потрачено в продуктовых магазинах»), а также автоматически подставлять категорию магазина для новых товаров из этого магазина.

---

## 2. Текущее состояние (AS-IS)

**Domain layer:**
- `Merchant` уже имеет свойство `Category: MerchantCategory` и метод `UpdateCategory(MerchantCategory category)`.
- `MerchantCategory` — перечисление со значениями: `Undefined, GroceryStores, ClothingAndFootwear, Electronics, Cosmetics, Pharmacies, SportingGoods, ChildrenGoods, StationeryAndBooks, PetStores, HomeGoods, HouseholdGoods, ConstructionAndRepairMaterials, AutomotiveGoods, Jewelry, Flowers, Hobbies, GardenSupplies, MusicalInstruments, KitchenAccessories, HouseholdService`.

**Infrastructure layer:**
- `IMerchantRepository` имеет методы: `AddAsync`, `GetByIdAsync`, `GetByInnAsync`.
- `MerchantEntity` имеет поле `Category`. Конфигурация EF Core корректно маппит `MerchantCategory` как `int` в колонку `category`.
- В БД колонка `category` в таблице `merchants` уже существует (создана миграцией `20241019160000_initial_create.sql`).

**API layer:**
- Эндпоинты магазинов встроены в группу `/api/receipts`: `PUT /api/receipts/merchants/{merchantId}/name` (обновление названия).
- Отдельной группы `/api/merchants` нет.
- Нет эндпоинта для получения списка **всех** магазинов.
- Нет эндпоинта для обновления категории магазина.

**Frontend:**
- В `Sidebar.tsx` есть ссылки: «Чеки» (`/`) и «Товары» (`/commodities`).
- `CommodityTable.tsx` содержит готовый UI-паттерн для административного редактирования категорий (выпадающий список с категориями, кнопка «ред.», индикация сохранения). Этот паттерн следует переиспользовать.
- Тип `Merchant` уже существует в `types/receipt.ts`.
- Сервис `adminService.ts` и хук `useAdmin` уже реализованы для проверки прав администратора.

---

## 3. Конкретные шаги реализации

### Шаг 1. Расширить репозиторий `IMerchantRepository` (Domain + Infrastructure)

**Файлы:**
- `.../Domain/Modules/Merchants/IMerchantRepository.cs`
- `.../Infrastructure/Persistence/Postgres/MerchantRepository.cs`

**Добавить методы:**
```csharp
Task<IReadOnlyCollection<Merchant>> GetAllAsync(CancellationToken cancellationToken = default);
Task UpdateCategoryAsync(Guid merchantId, MerchantCategory category, CancellationToken cancellationToken = default);
```

В реализации `MerchantRepository`:
- `GetAllAsync` — `_dbContext.Merchants.AsNoTracking().Select(m => m.MapToDomain()).ToListAsync()`.
- `UpdateCategoryAsync` — найти сущность по `merchantId`, обновить `entity.Category`, вызвать `SaveChangesAsync`. Если не найдена — выбросить или вернуть `false`.

### Шаг 2. Создать DTO для Merchant (если нужен расширенный) или использовать существующий `MerchantDto`

Существующий `MerchantDto` в `Application/Modules/Receipts/Models/MerchantDto.cs` подходит:
```csharp
public sealed record MerchantDto(Guid Id, string Name, int Category, string? Address, string? Inn);
```
Он уже используется в `ReceiptSummaryDto`. Можно переиспользовать его или, для чистоты архитектуры, вынести в отдельную папку `Application/Modules/Merchants/Models/`. На усмотрение разработчика.

### Шаг 3. Создать API-эндпоинты для Merchants (API Layer)

**Файл:** `.../Api/Modules/Merchants/MerchantEndpoints.cs` (новый файл)

Создать группу `/api/merchants` с эндпоинтами:

1. **`GET /api/merchants`** — список всех магазинов (только для администратора).
   - Проверить `UserContext.UserId`, проверить `user.IsAdmin`.
   - Вернуть `MerchantDto[]`.
   - Query-параметры: `limit` (default 50), `offset` (default 0), опционально `search` для фильтрации по имени.
   - Заголовок `X-Total-Count`.

2. **`PUT /api/merchants/{merchantId:guid}/category`** — обновление категории магазина (только для администратора).
   - Тело запроса: `{ "categoryId": int }`.
   - Проверить права администратора.
   - Проверить, что `categoryId` — валидное значение `MerchantCategory`.
   - Вызвать `merchantRepository.UpdateCategoryAsync(...)`.
   - Вернуть `Ok(new { categoryId, categoryName })`.

**Рекомендация:** Можно вынести проверку прав администратора в отдельный вспомогательный метод или middleware, чтобы не дублировать код.

Зарегистрировать эндпоинты в `Program.cs`: `app.MapMerchantEndpoints();`

### Шаг 4. Создать frontend API-функции для merchants

**Файл:** `frontend/src/api/merchants.ts` (новый файл)

```typescript
// types
export interface MerchantDto {
  id: string;
  name: string;
  category: number;
  address: string | null;
  inn: string | null;
}

export interface PaginatedMerchants {
  merchants: MerchantDto[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}

// fetch functions
export async function fetchMerchants({ limit, offset, signal }): Promise<PaginatedMerchants>
export async function updateMerchantCategory(merchantId: string, categoryId: number): Promise<void>
export async function fetchMerchantCategories(): Promise<Category[]>
```

Примечание: категории для merchants — это `MerchantCategory`. Можно получить их из `CommodityCategoryHelper`-подобного хелпера (который нужно будет создать для `MerchantCategory`) или, проще, вернуть на backend через отдельный эндпоинт. Рекомендуется создать на backend эндпоинт `GET /api/merchants/categories`, который вернёт все значения `MerchantCategory` с отображаемыми именами.

### Шаг 5. Создать React-компонент MerchantsPage

**Файл:** `frontend/src/components/MerchantsPage.tsx` (новый файл)
**Файл:** `frontend/src/components/MerchantTable.tsx` (новый файл, опционально — можно всё в одном файле)

`MerchantsPage.tsx`:
- Использовать `useAdmin()` для проверки прав.
- Если не администратор — показать сообщение «Доступ запрещён».
- Загружать список магазинов через `fetchMerchants()`.
- Выводить таблицу со столбцами: Название, Адрес, ИНН, Категория (с dropdown для админа), Действия.
- Использовать паттерн редактирования категории как в `CommodityTable.tsx`:
  - Показывать текущую категорию (или «Не указана»).
  - Кнопка «ред.» → появляется `<select>` со значением по умолчанию и списком всех категорий.
  - При выборе — отправлять `PUT /api/merchants/{id}/category` и обновлять список.
  - Отображать состояние загрузки («Сохранение...»).
- Использовать готовые компоненты: `Pagination`, `CustomDialog` (если нужно подтверждение).

`MerchantTable.tsx` (опционально):
- Вынести таблицу в отдельный компонент для переиспользования.
- Props: `merchants: MerchantDto[]`, `isAdmin: boolean`, `onRefresh: () => void`.

### Шаг 6. Добавить маршрут и навигацию

**Файл:** `frontend/src/App.tsx`
- Добавить маршрут `<Route path="/merchants" element={<MerchantsPage />} />` внутрь `<Route element={<Layout />}>`.

**Файл:** `frontend/src/components/Sidebar.tsx`
- Добавить пункт меню «Магазины» со ссылкой `/merchants`.
- Показывать этот пункт только администраторам (через `useAdmin()`).

### Шаг 7. Создать MerchantCategoryHelper (Backend)

По аналогии с `CommodityCategoryHelper` создать `MerchantCategoryHelper` в Domain или Application слое:
- Статический словарь `DisplayNames` с русскими отображаемыми именами для каждого значения `MerchantCategory`.
- Метод `GetDisplayName(MerchantCategory category)`.
- Метод `GetAll()` для получения списка `(MerchantCategory Id, string Name)`.

Если такой хелпер уже существует — нужно проверить. В коде я его не нашёл.

Добавить эндпоинт `GET /api/merchants/categories`, который вернёт список категорий для отображения в UI.

### Шаг 8. Валидация

- На backend: проверить, что `categoryId` — валидное значение `MerchantCategory` (`Enum.IsDefined`).
- Если `categoryId == 0` (`MerchantCategory.Undefined`) — разрешить (сброс категории).
- Проверить, что пользователь аутентифицирован и является администратором.
- Если магазин не найден — вернуть `404`.

### Шаг 9. Тестирование

**Backend (модульные тесты):**
- Файл: `.../tests/ReceiptCollector.Analytics.Api.Tests/MerchantEndpointsTests.cs` (расширить существующий).
- Тест-кейсы:
  1. `GetAllMerchants_WithAdminUser_ReturnsMerchantList` — проверить, что возвращается список.
  2. `GetAllMerchants_WithNonAdminUser_ReturnsForbidden`.
  3. `UpdateMerchantCategory_WithAdminUser_UpdatesSuccessfully` — проверить вызов репозитория.
  4. `UpdateMerchantCategory_WithInvalidCategory_ReturnsBadRequest`.
  5. `UpdateMerchantCategory_WithNonExistentMerchant_ReturnsNotFound`.
  6. `GetMerchantCategories_ReturnsAllCategories` — проверить, что все категории возвращаются с корректными именами.

**Frontend (если есть тесты):**
- Проверить, что `MerchantsPage` отображается только для администратора.
- Проверить, что вызов `updateMerchantCategory` отправляет корректный запрос.

---

## 4. Что нужно создать / модифицировать

| Слой | Файл | Действие |
|------|------|----------|
| Domain | `IMerchantRepository.cs` | Добавить `GetAllAsync`, `UpdateCategoryAsync` |
| Infrastructure | `MerchantRepository.cs` | Реализовать новые методы |
| Infrastructure | `DependencyInjectionExtensions.cs` | Возможно, не требует изменений |
| Application | — | Создать папку `Merchants/`, опционально `MerchantCategoryHelper` |
| API | `MerchantEndpoints.cs` (новый) | Эндпоинты `GET /api/merchants`, `PUT /api/merchants/{id}/category`, `GET /api/merchants/categories` |
| API | `Program.cs` | Добавить `app.MapMerchantEndpoints()` |
| Frontend | `api/merchants.ts` (новый) | Функции `fetchMerchants`, `updateMerchantCategory`, `fetchMerchantCategories` |
| Frontend | `components/MerchantsPage.tsx` (новый) | Страница управления магазинами |
| Frontend | `App.tsx` | Добавить маршрут `/merchants` |
| Frontend | `components/Sidebar.tsx` | Добавить ссылку «Магазины» (только для админа) |
| Tests | `MerchantEndpointsTests.cs` | Добавить тесты для новых эндпоинтов |

---

## 5. Требования к миграции БД

Колонка `category` в таблице `merchants` **уже существует**. Дополнительная миграция не требуется.

Однако если необходимо добавить seed-данные или изменить ограничения — создать новый SQL-скрипт в `.../Migrations/Scripts/` и зарегистрировать его в `MigrationRunner.cs`.

---

## 6. Правила валидации

- `categoryId` должен быть валидным значением `MerchantCategory` (значения 0–20). Если передан `null` или значение за пределами — `400 Bad Request`.
- Только аутентифицированные пользователи с `isAdmin == true` могут просматривать список магазинов и менять категории.
- При `categoryId == 0` (`Undefined`) — категория сбрасывается (это валидное действие).
- `limit` должен быть > 0, `offset` >= 0.

---

## 7. Рекомендации по реализации

- **Переиспользовать существующий паттерн:** UI-логика редактирования категории в `CommodityTable.tsx` — готовый референс. Сделать аналогично для магазинов.
- **MerchantCategoryHelper:** Создать по аналогии с `CommodityCategoryHelper`, поместить в `Domain/Modules/Merchants/`. Русские названия категорий уже есть в `CommodityCategoryHelper` — можно ориентироваться на них.
- **Ручное тестирование:** После реализации проверить:
  1. Вход под обычным пользователем — пункт «Магазины» не отображается.
  2. Вход под администратором — пункт «Магазины» отображается.
  3. Список магазинов загружается корректно.
  4. Изменение категории магазина сохраняется и отображается после обновления.
  5. Категория `Undefined` корректно отображается как «Не указана».
- **Graceful degradation:** Если API недоступен — показывать понятное сообщение об ошибке.
