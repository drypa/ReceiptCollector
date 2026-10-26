namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Пакетное сохранение категорий невозможно: часть запрошенных товаров не принадлежит
/// текущему пользователю или больше не существует (ADR-028 п. 1.2, 9).
/// <para>
/// В тексте — только количество недоступных строк: идентификаторы в ответ API не попадают,
/// чтобы не раскрывать существование чужих товаров (ADR-028 п. 10).
/// </para>
/// </summary>
public sealed class CommoditiesNotAccessibleException : Exception
{
    /// <summary>Сколько запрошенных товаров не найдено среди товаров текущего пользователя.</summary>
    public int NotAccessibleCount { get; }

    public CommoditiesNotAccessibleException(int notAccessibleCount)
        : base($"Some commodities are not accessible for the current user (count: {notAccessibleCount}).")
    {
        NotAccessibleCount = notAccessibleCount;
    }
}
