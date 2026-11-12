using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Админская бизнес-логика слов категоризации (ADR-035 п. 6–7, FR-3/FR-4/FR-5): полный снимок
/// по всем категориям, добавление с валидацией/дубликатом/лимитом и адресное удаление,
/// инвалидация singleton-кэша строго после commit мутации (NFR-4).
/// </summary>
/// <remarks>
/// Порядок проверок <see cref="AddAsync"/> — ровно таблицe ошибок ADR-035 п. 7: категория →
/// <c>listType</c> → слово после trim → дубликат → лимит. Первые три проверки эндпоинт выполняет
/// сам и отвечает <c>400</c> с текстом из таблицы; здесь они повторяются защитным
/// <see cref="ArgumentException"/> — сервис не доверяет вызывающему (прецедент
/// <c>PrecategorizedCommodityService.UpdateCategoryAsync</c>, D11), поэтому порядок и смысл
/// проверок не зависят от того, кто вызвал сервис.
/// Инвалидация кэша — best-effort после commit: мутация уже зафиксирована в БД (источник
/// правды), поэтому сбой перечитывания не отменяет успех — кэш остаётся «грязным» и
/// перечитывается на следующем чтении (ADR-035 п. 6).
/// </remarks>
internal sealed class CommodityCategoryKeywordService : ICommodityCategoryKeywordService
{
    private readonly ICommodityCategoryKeywordRepository _repository;
    private readonly ICommodityCategoryKeywordCache _cache;
    private readonly ILogger<CommodityCategoryKeywordService> _logger;

    public CommodityCategoryKeywordService(
        ICommodityCategoryKeywordRepository repository,
        ICommodityCategoryKeywordCache cache,
        ILogger<CommodityCategoryKeywordService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyCollection<CategoryKeywordsDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        // GET админа отражает БД (источник правды), а не снимок кэша: страница после мутации
        // делает рефетч (H1) и обязана увидеть зафиксированные данные даже при «грязном» кэше.
        var keywords = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var byList = keywords.ToLookup(k => (k.Category, k.ListType));

        // Порядок категорий — порядок объявления в реестре (ADR-035 п. 7); Undefined в выдаче нет
        // (слова с Undefined репозиторий уже отбрасывает, и категории такой нет в UI).
        var result = new List<CategoryKeywordsDto>();
        foreach (var (category, displayName) in CommodityCategoryHelper.GetAll())
        {
            if (category == CommodityCategory.Undefined)
            {
                continue;
            }

            result.Add(new CategoryKeywordsDto(
                (int)category,
                category.ToString(),
                displayName,
                CommodityCategoryHelper.GetGroup(category),
                SortedWords(byList, category, KeywordListType.Boost),
                SortedWords(byList, category, KeywordListType.Exclude)));
        }

        return result;
    }

    public async Task<AddKeywordResult> AddAsync(
        int categoryId,
        string? listType,
        string? word,
        CancellationToken cancellationToken = default)
    {
        // Порядок проверок — ровно таблица ADR-035 п. 7 (см. remarks). Эндпоинт уже отклонил
        // такие значения 400-ми; здесь — защита прямого/будущего вызова сервиса.
        // Тексты этих исключений — для логов/отладки и в HTTP-ответ НЕ попадают: контрактные
        // тексты 400 формирует эндпоинт по таблице ADR-035 п. 7.

        // 1. Категория: только валидный код реестра, Undefined запрещён (CHECK category_id <> 0).
        if (categoryId == (int)CommodityCategory.Undefined
            || !Enum.IsDefined(typeof(CommodityCategory), categoryId))
        {
            throw new ArgumentException(
                "Undefined or unknown category cannot be addressed in the keyword storage.",
                nameof(categoryId));
        }

        // 2. Тип списка: закрытое множество wire-имён boost/exclude (FR-1).
        if (!KeywordListTypeNames.TryParse(listType, out var parsedListType))
        {
            throw new ArgumentException(
                "Keyword list type must be 'boost' or 'exclude'.",
                nameof(listType));
        }

        // 3. Слово: 2–64 символа после trim (FR-5); регистр и внутренние символы сохраняются.
        if (!CommodityCategoryKeywordRules.IsValidWord(word))
        {
            throw new ArgumentException(
                "Word must be between 2 and 64 characters after trimming.",
                nameof(word));
        }

        var category = (CommodityCategory)categoryId;
        var trimmedWord = word!.Trim();
        var normalizedWord = CommodityCategoryKeywordRules.NormalizeWord(word);

        // 3a. Страховка: приведение к нижнему регистру МОЖЕТ расширить строку при смене
        // нормализации (например, переходе на full case folding: «İ» U+0130 → «i» + U+0307)
        // или сужении колонки — тогда слово из 64 символов дало бы normalized_word длиннее
        // varchar(64) схемы. В БД это 22001 (string data right truncation) — не 23505,
        // репозиторий его не перехватывает, и админ получил бы 500 вместо 400. Отклоняем
        // такое слово как невалидное: тот же класс ошибки и тот же текст 400, что у проверки
        // длины слова (эндпоинт повторяет эту проверку до вызова сервиса). На текущем рантайме
        // .NET (проверено на 10.0.7, Unix/ICU) ToLowerInvariant использует простые 1:1
        // регистропреобразования и расширения не даёт — гард остаётся дешёвой страховкой.
        if (!NormalizedWordFitsColumn(normalizedWord))
        {
            throw new ArgumentException(
                "Word must be between 2 and 64 characters after trimming.",
                nameof(word));
        }

        // 4. Дубликат в этой же паре «категория + список» (case-insensitive по normalized_word,
        // FR-5) — адресная проверка: то же слово в другой категории/списке не мешает.
        if (await _repository.ExistsAsync(category, parsedListType, normalizedWord, cancellationToken)
                .ConfigureAwait(false))
        {
            return AddKeywordResult.Duplicate();
        }

        // 5. Лимит 200 слов на список (FR-5, A5): count перед вставкой; единичный «перелёт»
        // при гонке допустим и самоблокируется (следующая вставка снова увидит >= 200).
        var currentCount = await _repository.CountAsync(category, parsedListType, cancellationToken)
            .ConfigureAwait(false);
        if (currentCount >= CommodityCategoryKeywordRules.MaxWordsPerList)
        {
            return AddKeywordResult.LimitReached();
        }

        // Id генерируется здесь (а не репозиторием): он обязан попасть в тело ответа POST
        // (ADR-035 п. 7), а контракт AddAsync возвращает только bool (отклонение задачи 03).
        var keyword = new CommodityCategoryKeyword(
            Guid.NewGuid(),
            category,
            parsedListType,
            trimmedWord,
            normalizedWord);

        // false — гонка двух вкладок: уникальный индекс (23505) отклонил дубликат, который
        // предпроверка не увидела. Это 409 (Duplicate), а не 500 (ADR-035 п. 7).
        if (!await _repository.AddAsync(keyword, cancellationToken).ConfigureAwait(false))
        {
            return AddKeywordResult.Duplicate();
        }

        // Инвалидация строго ПОСЛЕ commit (ADR-035 п. 6, порядок как cache.TryAdd в ADR 019/027).
        await RefreshCacheBestEffortAsync().ConfigureAwait(false);

        return AddKeywordResult.Added(
            keyword.Id,
            categoryId,
            KeywordListTypeNames.ToWireName(parsedListType),
            trimmedWord);
    }

    public async Task<DeleteKeywordOutcome> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Нет строки (повторный DELETE из другой вкладки) — false → NotFound (404), кэш не трогаем.
        if (!await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false))
        {
            return DeleteKeywordOutcome.NotFound;
        }

        await RefreshCacheBestEffortAsync().ConfigureAwait(false);

        return DeleteKeywordOutcome.Deleted;
    }

    /// <summary>
    /// Полное перечитывание кэша после commit. Штатная реализация (<c>InMemoryCommodityCategoryKeywordCache</c>)
    /// сбой не бросает — деградирует в «грязный» флаг (отклонение ревью T-03); защитный
    /// <c>catch</c> гарантирует «мутация не откатывается из-за сбоя инвалидации» и при нарушении
    /// контракта (прецедент <c>RecordDecisionsBestEffortAsync</c>, ADR-033).
    /// <para>
    /// Вызывается с <see cref="CancellationToken.None"/>: мутация к этому моменту уже зафиксирована,
    /// и инвалидация не должна отменяться обрывом клиентского соединения — иначе кэш «залип бы»
    /// «грязным»/устаревшим из-за отменённого запроса клиента, а не БД (NFR-4).
    /// </para>
    /// </summary>
    private async Task RefreshCacheBestEffortAsync()
    {
        try
        {
            await _cache.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Данные уже зафиксированы в БД (источник правды): мутация остаётся успешной,
            // кэш останется «грязным» и перечитается на следующем чтении (NFR-4 без рестарта).
            _logger.LogWarning(
                ex,
                "Commodity category keyword cache refresh failed after a committed mutation; the cache stays dirty.");
        }
    }

    /// <summary>
    /// Помещается ли нормализованное слово в колонку <c>normalized_word varchar(64)</c>:
    /// длина — в символах (кодовых точках), как <c>char_length</c> в PostgreSQL и в
    /// <see cref="CommodityCategoryKeywordRules.IsValidWord"/> (суррогатная пара — один символ).
    /// Вынесено из <see cref="AddAsync"/> как отдельное правило-страховка: на текущем рантайме
    /// .NET <c>ToLowerInvariant()</c> использует простые 1:1 регистропреобразования и строку
    /// не расширяет, но при смене нормализации (например, full case folding) или сужении
    /// колонки слово, прошедшее <see cref="CommodityCategoryKeywordRules.IsValidWord"/>,
    /// не должно доходить до вставки с ошибкой 22001, которую репозиторий не перехватывает
    /// (только 23505).
    /// </summary>
    internal static bool NormalizedWordFitsColumn(string normalizedWord)
        => normalizedWord.EnumerateRunes().Count() <= CommodityCategoryKeywordRules.MaxWordLength;

    /// <summary>
    /// Слова пары «категория + список» в детерминированном порядке выдачи (ADR-035 п. 7):
    /// по <c>word</c> OrdinalIgnoreCase, затем Ordinal как разрешение ничьих
    /// (например, «Яблоко» до «абрикос», «apple» и «Apple» рядом).
    /// </summary>
    private static IReadOnlyList<string> SortedWords(
        ILookup<(CommodityCategory Category, KeywordListType ListType), CommodityCategoryKeyword> byList,
        CommodityCategory category,
        KeywordListType listType)
        => byList[(category, listType)]
            .Select(keyword => keyword.Word)
            .OrderBy(word => word, StringComparer.OrdinalIgnoreCase)
            .ThenBy(word => word, StringComparer.Ordinal)
            .ToList();
}
