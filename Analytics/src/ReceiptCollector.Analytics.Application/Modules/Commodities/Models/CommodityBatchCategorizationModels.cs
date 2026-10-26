using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

/// <summary>
/// Результат порционного suggest для страницы «Товары» (ADR-028 п. 1.1).
/// <para>
/// <see cref="Items"/> — по одной записи на каждую найденную строку (включая повторяющиеся
/// названия; порядок не гарантирован, клиент сопоставляет по <c>commodityId</c>). Форма элемента
/// переиспользует <see cref="ReceiptItemCategorizationResult"/> — wire-формат идентичен suggest чека.
/// </para>
/// <para>
/// <see cref="AiAvailable"/> — <c>!string.IsNullOrWhiteSpace(AI:BaseUrl)</c>. При <c>false</c> ИИ
/// не вызывается, остаток помечается <c>undefined</c>; эндпоинт всё равно отвечает <c>200</c> (C7).
/// </para>
/// </summary>
public sealed record CommodityBatchCategorizationResult(
    IReadOnlyCollection<ReceiptItemCategorizationResult> Items,
    bool AiAvailable);

/// <summary>
/// Одно assign-only изменение категории при пакетном сохранении (ADR-028 п. 1.2):
/// <see cref="CommodityCategory.Undefined"/> запрещён, сброс категории этим контрактом не поддерживается.
/// </summary>
public sealed record CommodityCategoryChange(Guid CommodityId, CommodityCategory Category);
