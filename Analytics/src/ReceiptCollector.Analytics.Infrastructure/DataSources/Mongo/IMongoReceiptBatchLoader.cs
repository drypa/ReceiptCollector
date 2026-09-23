using MongoDB.Bson;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

public interface IMongoReceiptBatchLoader
{
    /// <summary>
    /// Страница документов raw_tickets в формате keyset-пагинации по _id (D7):
    /// фильтр <c>{"_id": {"$gt": afterId}, "ticket": {"$ne": null}}</c>, сортировка по _id,
    /// limit = batchSize. Первая страница — afterId = ObjectId.Empty.
    /// <remarks>
    /// MongoDB-семантика: <c>$ne: null</c> не возвращает документы ни с явным
    /// <c>ticket: null</c>, ни без ключа <c>ticket</c> вовсе (отсутствующий ключ
    /// трактуется как null) — legacy-документы 2020 (верхнеуровневый receipt) не попадают
    /// в выборку (требование 1 декомпозиции).
    /// </remarks>
    /// </summary>
    Task<IReadOnlyList<RawTicketDocument>> LoadPageAsync(ObjectId afterId, int batchSize, CancellationToken cancellationToken);
}