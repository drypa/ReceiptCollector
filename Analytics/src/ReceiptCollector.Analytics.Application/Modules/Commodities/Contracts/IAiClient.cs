using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Порт доступа к AI-модели (OpenAI-совместимый HTTP API, ADR 009 решение C6).
/// Реализация живёт в Infrastructure (OpenAiCompatibleAiClient), чтобы Application
/// оставался независимым от HTTP/транспорта.
/// </summary>
public interface IAiClient
{
    /// <summary>
    /// Определяет категорию товара по его названию из фиксированного реестра категорий.
    /// </summary>
    /// <param name="productName">Название товара из чека.</param>
    /// <param name="categoryNames">Реестр допустимых категорий (без «Не указана») — имена enum.</param>
    /// <returns>Категория из реестра, не равная Undefined.</returns>
    /// <exception cref="AiClientException">Сетевые/временные ошибки, таймаут или нераспознанный ответ.</exception>
    Task<AiSuggestionResult> SuggestCategoryAsync(
        string productName,
        IReadOnlyCollection<string> categoryNames,
        CancellationToken cancellationToken = default);
}

/// <summary>Ошибка обращения к AI-модели (сеть, таймаут, невалидный ответ).</summary>
public class AiClientException : Exception
{
    public AiClientException(string message)
        : base(message)
    {
    }

    public AiClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}