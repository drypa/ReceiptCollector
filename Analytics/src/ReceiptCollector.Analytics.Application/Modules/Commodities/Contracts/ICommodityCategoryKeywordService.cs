using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Админская бизнес-логика слов категоризации (ADR-035 п. 6–7, FR-3/FR-4/FR-5/FR-10):
/// полный снимок по всем категориям, адресное добавление/удаление слова с валидацией,
/// проверкой дубликата и лимита, и инвалидацией in-memory кэша после commit (NFR-4).
/// Права администратора проверяет эндпоинт (<c>AdminAuthorization</c>) — сервис вызывается
/// только для авторизованного администратора.
/// </summary>
public interface ICommodityCategoryKeywordService
{
    /// <summary>
    /// Полный снимок слов по <b>всем</b> определённым категориям (кроме
    /// <see cref="Domain.Modules.Commodities.CommodityCategory.Undefined"/>), включая категории
    /// с пустыми списками (UC-1: плейсхолдеры и счётчики видны без второго запроса).
    /// Порядок категорий — порядок объявления в <c>CommodityCategoryHelper.GetAll()</c>,
    /// слова — по <c>word</c> (<c>OrdinalIgnoreCase</c>, затем <c>Ordinal</c>) — детерминированно
    /// (ADR-035 п. 7).
    /// </summary>
    Task<IReadOnlyCollection<CategoryKeywordsDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавление слова в пару «категория + список» (FR-4/FR-5). Значения — wire-формат API,
    /// сервис не доверяет вызывающему и выполняет проверки строго в порядке таблицы ошибок
    /// ADR-035 п. 7: категория → <c>listType</c> → слово после trim → дубликат → лимит.
    /// Проверка слова включает длину после trim и то, что нормализованная форма помещается
    /// в колонку (страховка на случай смены нормализации или сужения колонки). Первые три
    /// проверки — защитные (<see cref="ArgumentException"/>): эндпоинт отклоняет такие значения
    /// <c>400</c> до вызова сервиса (прецедент <c>PrecategorizedCommodityService.UpdateCategoryAsync</c>),
    /// а тексты исключений в HTTP-ответ не уходят — контрактные тексты <c>400</c> формирует
    /// эндпоинт по таблице ADR-035 п. 7.
    /// <para>
    /// Дубликат — case-insensitive по нормализованному слову в этой же паре
    /// (<see cref="AddKeywordOutcome.Duplicate"/>), в том числе гонка двух вкладок:
    /// <c>false</c> из репозитория (unique violation 23505) тоже даёт
    /// <see cref="AddKeywordOutcome.Duplicate"/>, а не <c>500</c> (ADR-035 п. 7).
    /// Лимит <c>MaxWordsPerList</c> считается по паре перед вставкой
    /// (<see cref="AddKeywordOutcome.LimitReached"/>).
    /// </para>
    /// <para>
    /// После успешного commit — инвалидация кэша (<c>RefreshAsync</c>); сбой перечитывания
    /// не меняет исход мутации: данные уже зафиксированы, кэш останется «грязным» и
    /// перечитается на следующем чтении (ADR-035 п. 6, NFR-4).
    /// </para>
    /// </summary>
    /// <param name="categoryId">Числовой код <c>CommodityCategory</c>; <c>Undefined</c> запрещён.</param>
    /// <param name="listType">Wire-имя списка (<c>boost</c>/<c>exclude</c>).</param>
    /// <param name="word">Слово как введено; сохраняются регистр и внутренние символы, обрамляющие пробелы снимаются (FR-5).</param>
    Task<AddKeywordResult> AddAsync(
        int categoryId,
        string? listType,
        string? word,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаление слова по <c>id</c> (адресное, G1). Нет строки (в т.ч. удалена в другой вкладке) —
    /// <see cref="DeleteKeywordOutcome.NotFound"/>. После успешного удаления — инвалидация кэша
    /// с той же семантикой деградации, что у <see cref="AddAsync"/>.
    /// </summary>
    Task<DeleteKeywordOutcome> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
