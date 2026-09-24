using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.AI;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Tests;

public class OpenAiCompatibleAiClientTests
{
    private static readonly string[] CategoryCatalog = ["Food", "Dairy", "Beverages"];

    private static OpenAiCompatibleAiClient CreateClient(
        HttpMessageHandler handler,
        AiOptions? options = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new OpenAiCompatibleAiClient(
            httpClient,
            Options.Create(options ?? new AiOptions { BaseUrl = "http://ai.local/v1" }),
            NullLogger<OpenAiCompatibleAiClient>.Instance);
    }

    private static HttpMessageHandler CreateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        int maxCalls = 10)
    {
        var calls = 0;
        return new StubHttpMessageHandler(request =>
        {
            calls++;
            if (calls > maxCalls)
            {
                throw new InvalidOperationException("The handler was called more times than expected.");
            }

            return respond(request);
        });
    }

    private static HttpResponseMessage JsonResponse(string jsonBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task SuggestCategoryAsync_parses_category_from_json_content()
    {
        var handler = CreateHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"Food\"}"}}]}"""));

        var client = CreateClient(handler);
        var result = await client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None);

        Assert.Equal(CommodityCategory.Food, result.Category);
    }

    [Fact]
    public async Task SuggestCategoryAsync_accepts_lowercase_enum_name()
    {
        var handler = CreateHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"dairy\"}"}}]}"""));

        var client = CreateClient(handler);
        var result = await client.SuggestCategoryAsync("Молоко", CategoryCatalog, CancellationToken.None);

        Assert.Equal(CommodityCategory.Dairy, result.Category);
    }

    [Fact]
    public async Task SuggestCategoryAsync_parses_content_inside_markdown_code_fence()
    {
        var handler = CreateHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"```json\n{\"category\": \"Beverages\"}\n```"}}]}"""));

        var client = CreateClient(handler);
        var result = await client.SuggestCategoryAsync("Кола", CategoryCatalog, CancellationToken.None);

        Assert.Equal(CommodityCategory.Beverages, result.Category);
    }

    [Fact]
    public async Task SuggestCategoryAsync_rejects_numeric_category()
    {
        var handler = CreateHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"19\"}"}}]}"""));

        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<AiClientException>(() =>
            client.SuggestCategoryAsync("Молоко", CategoryCatalog, CancellationToken.None));

        Assert.Contains("category", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestCategoryAsync_rejects_undefined_category()
    {
        var handler = CreateHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"Undefined\"}"}}]}"""));

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<AiClientException>(() =>
            client.SuggestCategoryAsync("Молоко", CategoryCatalog, CancellationToken.None));
    }

    [Fact]
    public async Task SuggestCategoryAsync_retries_once_on_network_error()
    {
        var attempts = 0;
        var handler = CreateHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("Connection refused");
            }

            return JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"Food\"}"}}]}""");
        });

        var client = CreateClient(handler);
        var result = await client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None);

        Assert.Equal(CommodityCategory.Food, result.Category);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task SuggestCategoryAsync_retries_once_on_http_503()
    {
        var attempts = 0;
        var handler = CreateHandler(_ =>
        {
            attempts++;
            return attempts == 1
                ? JsonResponse("""{"error":"overloaded"}""", HttpStatusCode.ServiceUnavailable)
                : JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"Food\"}"}}]}""");
        });

        var client = CreateClient(handler);
        var result = await client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None);

        Assert.Equal(CommodityCategory.Food, result.Category);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task SuggestCategoryAsync_throws_after_two_transient_failures()
    {
        var handler = CreateHandler(_ => throw new HttpRequestException("Network is down"));

        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<AiClientException>(() =>
            client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None));

        Assert.Contains("2 attempts", exception.Message);
    }

    [Fact]
    public async Task SuggestCategoryAsync_does_not_retry_on_invalid_json()
    {
        var handler = CreateHandler(_ =>
            JsonResponse("""{"choices":[{"message":{"content":"не json"}}]}"""));

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<AiClientException>(() =>
            client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None));
    }

    [Fact]
    public async Task SuggestCategoryAsync_throws_on_http_400_without_retry()
    {
        var attempts = 0;
        var handler = CreateHandler(_ =>
        {
            attempts++;
            return JsonResponse("""{"error":"bad request"}""", HttpStatusCode.BadRequest);
        });

        var client = CreateClient(handler);

        await Assert.ThrowsAsync<AiClientException>(() =>
            client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task SuggestCategoryAsync_sends_bearer_token_when_api_key_configured()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = CreateHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"Food\"}"}}]}""");
        });

        var client = CreateClient(handler, new AiOptions
        {
            BaseUrl = "http://ai.local/v1",
            ApiKey = "secret-key"
        });

        await client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "secret-key"), capturedRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task SuggestCategoryAsync_posts_to_chat_completions_endpoint_with_model()
    {
        string? capturedRequestUri = null;
        var requestBody = string.Empty;
        var handler = CreateHandler(request =>
        {
            capturedRequestUri = request.RequestUri?.ToString();
            requestBody = request.Content != null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : string.Empty;
            return JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"Food\"}"}}]}""");
        });

        var client = CreateClient(handler, new AiOptions { BaseUrl = "http://ai.local/v1/", Model = "qwen2.5" });

        await client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None);

        Assert.Equal("http://ai.local/v1/chat/completions", capturedRequestUri);
        Assert.Contains("\"model\":\"qwen2.5\"", requestBody);
        Assert.Contains("НазваниеКатегории", requestBody);
    }

    [Fact]
    public async Task SuggestCategoryAsync_prompt_contains_new_category_heuristics()
    {
        // ADR 022, решение C2: в промт добавлен блок эвристик-разграничений
        // новых категорий от базовых (Сухофрукты ≠ Фрукты, Соусы/Приправы ≠ Бакалея,
        // Колбасные изделия ≠ Мясо, Конфеты ≠ Кондитерские изделия, Консервы ≠ Бакалея).
        // Эвристики статичны, поэтому проверяем строки напрямую.
        var requestBody = string.Empty;
        var handler = CreateHandler(request =>
        {
            requestBody = request.Content != null
                ? request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                : string.Empty;
            return JsonResponse("""{"choices":[{"message":{"content":"{\"category\": \"Food\"}"}}]}""");
        });

        var client = CreateClient(handler);

        await client.SuggestCategoryAsync("Курага", CategoryCatalog, CancellationToken.None);

        Assert.Contains("Разграничения схожих категорий", requestBody);
        Assert.Contains("Сухофрукты", requestBody);
        Assert.Contains("DriedFruits", requestBody);
        Assert.Contains("не Fruits", requestBody);
        Assert.Contains("Соусы", requestBody);
        Assert.Contains("Sauces", requestBody);
        Assert.Contains("Приправы", requestBody);
        Assert.Contains("Spices", requestBody);
        Assert.Contains("не Groceries", requestBody);
        Assert.Contains("Sausages", requestBody);
        Assert.Contains("не Meat", requestBody);
        Assert.Contains("Sweets", requestBody);
        Assert.Contains("не Confectionery", requestBody);
        Assert.Contains("CannedFood", requestBody);
    }

    [Fact]
    public async Task SuggestCategoryAsync_throws_invalid_operation_when_base_url_empty()
    {
        var handler = CreateHandler(_ => throw new InvalidOperationException("Should not be called"));
        var client = CreateClient(handler, new AiOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SuggestCategoryAsync("Хлеб", CategoryCatalog, CancellationToken.None));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_respond(request));
        }
    }
}