namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

public interface IMongoUserLoader
{
    Task<IReadOnlyList<MongoUserDocumentDto>> LoadAllAsync(CancellationToken cancellationToken);
}
