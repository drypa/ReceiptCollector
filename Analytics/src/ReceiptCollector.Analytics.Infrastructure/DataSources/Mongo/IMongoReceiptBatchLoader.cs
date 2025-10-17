namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

public interface IMongoReceiptBatchLoader
{
    Task LoadAllAsync(int batchSize, CancellationToken cancellationToken);
}