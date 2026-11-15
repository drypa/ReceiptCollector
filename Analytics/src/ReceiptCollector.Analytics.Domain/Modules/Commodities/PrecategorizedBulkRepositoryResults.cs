namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Результат массового подтверждения по одной строке очереди на уровне репозитория
/// (ADR-036 §2.1, решение B2). Значения полей — из <b>снимка</b> строки, прочитанного одним
/// <c>SELECT … FOR UPDATE</c> внутри той же транзакции, поэтому применённая категория и данные
/// для журнала не могут разойтись (D1).
/// <para>
/// <see cref="Outcome"/>: <see cref="PrecategorizedOperationOutcome.Applied"/> — строка была
/// <c>proposed</c> и переведена в <c>applied</c> этой операцией;
/// <see cref="PrecategorizedOperationOutcome.Conflict"/> — строка уже <c>applied</c>
/// (пропуск <c>already-applied</c>); <see cref="PrecategorizedOperationOutcome.NotFound"/> —
/// строки нет (пропуск <c>not-found</c>). Пропуски — результат строки, а не ошибка транзакции (C1).
/// </para>
/// <para>
/// <see cref="Name"/> заполняется для строк, попавших в снимок (<c>Applied</c> и
/// <c>Conflict</c>), и равен <see langword="null"/> для <c>NotFound</c> — строки нет, взять
/// имя не из чего. <see cref="OriginalProposedCategory"/>, <see cref="ProposedCategory"/> и
/// <see cref="UpdatedCommodities"/> заполняются только для <c>Applied</c>-строк: у пропущенных
/// строк категория не применялась, значения <see langword="null"/>/<c>0</c> и не используются
/// (маппинг в wire-модель — задача сервиса, ADR-036 §3).
/// </para>
/// </summary>
public sealed record BulkConfirmRepositoryItem(
    Guid Id,
    PrecategorizedOperationOutcome Outcome,
    string? Name,
    CommodityCategory? OriginalProposedCategory,
    CommodityCategory? ProposedCategory,
    int UpdatedCommodities);

/// <summary>
/// Результат массового отклонения по одной строке очереди на уровне репозитория (ADR-036 §2.1).
/// <see cref="PrecategorizedOperationOutcome.Applied"/> — <c>proposed</c>-строка удалена;
/// <see cref="PrecategorizedOperationOutcome.Conflict"/> — уже <c>applied</c>
/// (<c>already-applied</c>); <see cref="PrecategorizedOperationOutcome.NotFound"/> — строки нет
/// (<c>not-found</c>). Счётчика обновлённых товаров нет: отклонение <c>commodities</c> не меняет.
/// </summary>
public sealed record BulkDeclineRepositoryItem(Guid Id, PrecategorizedOperationOutcome Outcome);
