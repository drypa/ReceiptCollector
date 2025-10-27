using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

[BsonIgnoreExtraElements]
public sealed class MongoUserDocumentDto
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("name")]
    public string Name { get; init; } = string.Empty;

    [BsonElement("telegram_id")]
    public int? TelegramId { get; init; }
}
