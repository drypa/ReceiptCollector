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
/// Ретрай: один повтор при сетевых ошибках, таймауте или HTTP 5xx. Невалидный
/// ответ/JSON не ретраится (тратит квоту впустую).
/// </summary>
internal sealed class OpenAiCompatibleAiClient : IAiClient
{
    private const int RetryCount = 1;

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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("AI categorization is not configured: 'AI:BaseUrl' is empty.");
        }

        var payload = BuildPayload(productName, categoryNames, _options.Model);

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

    private static string BuildPayload(string productName, IReadOnlyCollection<string> categoryNames, string modelName)
    {
        var catalog = categoryNames.Count > 0 ? string.Join(", ", categoryNames) : string.Empty;

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
                    content =
                        $"Товар: \"{productName}\"\n" +
                        $"Выбери ОДНУ наиболее подходящую категорию из реестра: {catalog}.\n" +
                        "Ответь строго в формате JSON: {\"category\": \"НазваниеКатегории\"} без пояснений.\n" +
                        "Разграничения схожих категорий:\n" +
                        "- Сухофрукты (DriedFruits): изюм, курага, чернослив, финики — не Fruits (свежие фрукты/ягоды).\n" +
                        "- Соусы (Sauces) и Приправы (Spices): кетчуп, майонез, соевый соус, специи — не Groceries " +
                        "(бакалея — крупы, мука, макароны, сахар, соль, масло, чай/кофе).\n" +
                        "- Колбасные изделия (Sausages): колбаса, сосиски, сардельки, ветчина — не Meat " +
                        "(свежее/замороженное мясо).\n" +
                        "- Конфеты (Sweets): конфеты, шоколад, мармелад, зефир, печенье — не Confectionery " +
                        "(торты, пирожные, выпечка, кексы).\n" +
                        "- Консервы (CannedFood): рыбные, мясные, овощные консервы, паштеты, джемы и варенья " +
                        "(в банках) — не Groceries (бакалея — крупы, мука, макароны, сахар, соль, масло, чай/кофе)."
                }
            }
        };

        return JsonSerializer.Serialize(body, JsonOptions);
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