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
    /// <param name="hints">
    /// Подсказки по ключевым словам от <see cref="ICommodityCategoryHintProvider"/> (ADR-035,
    /// решение D1): это данные запроса, а не инструкция — решение о категории по-прежнему
    /// принимает модель. Обязательный параметр без значения по умолчанию, чтобы новый вызов не мог
    /// забыть подсказки; пустая коллекция означает «совпадений нет», <c>null</c> не допускается.
    /// </param>
    /// <returns>Категория из реестра, не равная Undefined.</returns>
    /// <exception cref="AiClientException">Сетевые/временные ошибки, таймаут или нераспознанный ответ.</exception>
    Task<AiSuggestionResult> SuggestCategoryAsync(
        string productName,
        IReadOnlyCollection<string> categoryNames,
        IReadOnlyCollection<CommodityCategoryKeywordHint> hints,
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