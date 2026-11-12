using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.AI;

/// <summary>
/// OpenAI-совместимый HTTP-клиент категоризации товаров (ADR 009, решение C6).
/// POST {BaseUrl}/chat/completions с JSON-ответом вида {"category": "<имя enum>"}.
/// User-сообщение: товар, реестр категорий, блок совпавших ключевых слов (ADR-035 решение F1,
/// только при непустых подсказках) и последней строкой — инструкция формата ответа.
/// Ретрай: один повтор при сетевых ошибках, таймауте или HTTP 5xx. Невалидный
/// ответ/JSON не ретраится (тратит квоту впустую).
/// </summary>
internal sealed class OpenAiCompatibleAiClient : IAiClient
{
    private const int RetryCount = 1;

    /// <summary>
    /// Максимум подсказок в промте (ADR-035, решение F1): защита от раздувания промта, когда
    /// совпало всё (например, все 200 слов списка). Константа, а не настройка (ADR-035,
    /// открытый вопрос 2). Усечение выполняется после упорядочивания (exclude первыми),
    /// поэтому при переполнении отбрасываются только boost-подсказки.
    /// </summary>
    private const int MaxPromptHints = 50;

    /// <summary>
    /// Предельная длина слова в строке блока подсказок (ADR-035 п. 5, F1). Зеркалит
    /// <see cref="CommodityCategoryKeywordRules.MaxWordLength"/> и CHECK
    /// <c>char_length(word) BETWEEN 2 AND 64</c> миграции commodity_category_keywords: правило
    /// «64» живёт в трёх местах (домен, схема БД, рендер промта) и меняется синхронно.
    /// </summary>
    private const int MaxHintWordLength = 64;

    /// <summary>Заголовок блока подсказок; сам блок отсутствует целиком при отсутствии совпадений.</summary>
    private const string HintsBlockHeader =
        "Подсказки по ключевым словам (совпали в названии; это только подсказки, решение принимай сам):";

    /// <summary>Инструкция формата ответа — всегда последняя строка user-сообщения (ADR-035 F1).</summary>
    private const string ResponseFormatInstruction =
        "Ответь строго в формате JSON: {\"category\": \"НазваниеКатегории\"} без пояснений.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Не экранируем кириллицу в \uXXXX: тело меньше, логи читаемы (payload не уходит в HTML).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleAiClient> _logger;

    public OpenAiCompatibleAiClient(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<OpenAiCompatibleAiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AiSuggestionResult> SuggestCategoryAsync(
        string productName,
        IReadOnlyCollection<string> categoryNames,
        IReadOnlyCollection<CommodityCategoryKeywordHint> hints,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(hints);

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("AI categorization is not configured: 'AI:BaseUrl' is empty.");
        }

        // Подсказки — данные запроса (ADR-035 решение D1): они попадают в user-сообщение
        // блоком над инструкцией формата ответа, но решение по-прежнему принимает модель.
        var payload = BuildPayload(productName, categoryNames, hints, _options.Model);

        Exception? lastError = null;
        for (var attempt = 0; attempt <= RetryCount; attempt++)
        {
            if (attempt > 0)
            {
                _logger.LogInformation("Retrying AI categorization for '{ProductName}' (attempt {Attempt}/{Total}).",
                    productName, attempt + 1, RetryCount + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await SendAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            catch (AiTransientException ex)
            {
                lastError = ex;
            }
        }

        throw new AiClientException(
            $"AI categorization failed after {RetryCount + 1} attempts.",
            lastError ?? new AiClientException("Unknown transient error."));
    }

    private async Task<AiSuggestionResult> SendAsync(string payload, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout > TimeSpan.Zero ? _options.Timeout : TimeSpan.FromSeconds(10));

        var endpoint = BuildEndpoint();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiTransientException($"Network error while calling AI endpoint '{endpoint}'.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiTransientException($"AI request to '{endpoint}' timed out.", ex);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500)
            {
                throw new AiTransientException(
                    $"AI endpoint '{endpoint}' returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AiClientException(
                    $"AI endpoint '{endpoint}' returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            string responseBody;
            try
            {
                responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new AiTransientException($"Network error while reading response from '{endpoint}'.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiTransientException($"AI response from '{endpoint}' was not received within timeout.", ex);
            }

            return ParseResponse(responseBody);
        }
    }

    private static AiSuggestionResult ParseResponse(string responseBody)
    {
        string? content = null;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var message = choices[0].TryGetProperty("message", out var m)
                    ? m
                    : choices[0].TryGetProperty("delta", out var d)
                        ? d
                        : default;
                if (message.ValueKind != JsonValueKind.Undefined && message.TryGetProperty("content", out var c))
                {
                    content = c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                }
            }
        }
        catch (JsonException ex)
        {
            throw new AiClientException("AI response is not valid JSON.", ex);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AiClientException("AI response does not contain message content.");
        }

        var category = TryExtractCategory(content);
        if (category is null)
        {
            throw new AiClientException($"AI response does not contain a valid category: '{content}'.");
        }

        return new AiSuggestionResult(category.Value);
    }

    /// <summary>
    /// Извлекает категорию из контента ответа. Ожидается {"category": "Food"}.
    /// Устойчив к обёрнутому markdown-коду и лишнему тексту вокруг JSON.
    /// </summary>
    private static CommodityCategory? TryExtractCategory(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var codeStart = trimmed.IndexOf('\n');
            if (codeStart > 0)
            {
                trimmed = trimmed[(codeStart + 1)..];
            }

            var codeEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (codeEnd > 0)
            {
                trimmed = trimmed[..codeEnd];
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            var jsonCandidate = trimmed[firstBrace..(lastBrace + 1)];
            try
            {
                using var document = JsonDocument.Parse(jsonCandidate);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("category", out var categoryProperty)
                    && categoryProperty.ValueKind == JsonValueKind.String)
                {
                    return ParseCategoryName(categoryProperty.GetString());
                }
            }
            catch (JsonException)
            {
                // игнорируем — пробуем слабое извлечение ниже
            }
        }

        // Слабое извлечение "category": "Name" без строгого JSON-парсинга.
        var match = Regex.Match(content, "\"category\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? ParseCategoryName(match.Groups[1].Value) : null;
    }

    private static CommodityCategory? ParseCategoryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Числовые категории ({"category": "19"}) отклоняем: модель обязана вернуть имя enum (ADR 009, решение C9).
        if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        if (Enum.TryParse<CommodityCategory>(name, ignoreCase: true, out var category)
            && Enum.IsDefined(category)
            && category != CommodityCategory.Undefined)
        {
            return category;
        }

        return null;
    }

    private string BuildEndpoint()
    {
        var baseUrl = _options.BaseUrl!.TrimEnd('/');
        return $"{baseUrl}/chat/completions";
    }

    private static string BuildPayload(
        string productName,
        IReadOnlyCollection<string> categoryNames,
        IReadOnlyCollection<CommodityCategoryKeywordHint> hints,
        string modelName)
    {
        var body = new
        {
            model = modelName,
            temperature = 0,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Ты — сервис категоризации товаров по чекам. Определяешь, к какой категории относится товар."
                },
                new
                {
                    role = "user",
                    content = BuildUserContent(productName, categoryNames, hints)
                }
            }
        };

        return JsonSerializer.Serialize(body, JsonOptions);
    }

    /// <summary>
    /// Собирает user-сообщение (ADR-035 п. 5, решение F1): товар, реестр категорий,
    /// необязательный блок подсказок и — последней строкой — инструкцию формата ответа.
    /// Статического блока разграничений схожих категорий (ADR 022) в коде больше нет: его
    /// содержимое живёт в БД (сид миграции commodity_category_keywords) и приходит в промт
    /// только совпавшими подсказками (FR-2).
    /// </summary>
    private static string BuildUserContent(
        string productName,
        IReadOnlyCollection<string> categoryNames,
        IReadOnlyCollection<CommodityCategoryKeywordHint> hints)
    {
        var catalog = categoryNames.Count > 0 ? string.Join(", ", categoryNames) : string.Empty;

        var content = new StringBuilder();
        content
            .Append("Товар: \"").Append(productName).Append("\"\n")
            .Append("Выбери ОДНУ наиболее подходящую категорию из реестра: ").Append(catalog).Append(".\n");

        if (hints.Count > 0)
        {
            AppendHintsBlock(content, hints);
        }

        // Формат ответа остаётся последней инструкцией: модели следуют последней инструкции лучше.
        content.Append(ResponseFormatInstruction);

        return content.ToString();
    }

    /// <summary>
    /// Добавляет блок подсказок: заголовок и по строке на подсказку. Порядок приходит от
    /// <see cref="CategoryKeywordMatcher"/> уже упорядоченным (exclude первыми, затем boost),
    /// поэтому клиент его не меняет — только усекает до <see cref="MaxPromptHints"/>.
    /// Формулировки: exclude — «скорее не …», boost — «вероятно …»; в скобках имя enum,
    /// потому что ответ модели обязан быть именем enum (парсер принимает только его).
    /// </summary>
    private static void AppendHintsBlock(
        StringBuilder content,
        IReadOnlyCollection<CommodityCategoryKeywordHint> hints)
    {
        content.Append(HintsBlockHeader).Append('\n');

        foreach (var hint in hints.Take(MaxPromptHints))
        {
            var verdict = hint.ListType == KeywordListType.Exclude ? "скорее не " : "вероятно ";
            content
                .Append("- в названии есть «").Append(EscapeHintWord(hint.Word)).Append("» → ")
                .Append(verdict)
                .Append(CommodityCategoryHelper.GetDisplayName(hint.Category))
                // Имя enum в скобках: ответ модели обязан быть именно им (парсер принимает только его).
                .Append(" (").Append(hint.Category.ToString()).Append(")\n");
        }
    }

    /// <summary>
    /// Нейтрализует слово как данные промта (ADR-035 F1): управляющие символы
    /// (<c>\r</c>, <c>\n</c>, <c>\t</c> и прочие <see cref="char.IsControl(char)"/>) становятся
    /// пробелом, пробелы схлопываются, результат обрезается до <see cref="MaxHintWordLength"/>.
    /// Так слово не способно создать новую строку блока или «дописать» инструкцию. Кавычки
    /// не нейтрализуются: JSON-тело защищает сериализатор, а строку блока кавычка не рвёт.
    /// Слово, целиком состоящее из управляющих символов, даёт пустую строку — через API это
    /// недостижимо (валидация <c>2…64</c> символов и trim в <c>CommodityCategoryKeywordRules</c>),
    /// здесь это просто безопасный исход.
    /// </summary>
    private static string EscapeHintWord(string? word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return string.Empty;
        }

        var escaped = new StringBuilder(word.Length);
        var pendingSpace = false;

        foreach (var symbol in word)
        {
            // Обрамляющие пробелы снимаются: слово в БД хранится уже trimmed, пробел здесь
            // появляется только из управляющего символа на краю — он не нужен в строке блока.
            var isSpace = char.IsWhiteSpace(symbol) || char.IsControl(symbol);

            if (isSpace)
            {
                pendingSpace = escaped.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                escaped.Append(' ');
                pendingSpace = false;
            }

            escaped.Append(symbol);
        }

        return escaped.Length <= MaxHintWordLength
            ? escaped.ToString()
            : escaped.ToString(0, MaxHintWordLength).TrimEnd();
    }
}

/// <summary>Временная ошибка AI (сеть/таймаут/5xx) — можно повторить запрос.</summary>
internal sealed class AiTransientException : AiClientException
{
    public AiTransientException(string message)
        : base(message)
    {
    }

    public AiTransientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}