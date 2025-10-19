using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

[BsonIgnoreExtraElements]
public sealed class MongoReceiptDocumentDto
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("id")]
    public string ExternalId { get; init; } = string.Empty;

    [BsonElement("ticket_id")]
    public string TicketId { get; init; } = string.Empty;

    [BsonElement("query_string")]
    public string QueryString { get; init; } = string.Empty;

    [BsonElement("createdat")]
    public string CreatedAt { get; init; } = string.Empty;

    [BsonElement("kind")]
    public string Kind { get; init; } = string.Empty;

    [BsonElement("operation")]
    public OperationDto? Operation { get; init; }

    [BsonElement("qr")]
    public string Qr { get; init; } = string.Empty;

    [BsonElement("query")]
    public QueryDto? Query { get; init; }

    [BsonElement("seller")]
    public SellerDto? Seller { get; init; }

    [BsonElement("status")]
    public int Status { get; init; }

    [BsonElement("ticket")]
    public TicketDto? Ticket { get; init; }

    [BsonElement("owner")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Owner { get; init; } = string.Empty;

    [BsonElement("receipt")]
    public ReceiptDto? Receipt { get; init; }

    [BsonIgnoreExtraElements]
    public sealed class OperationDto
    {
        [BsonElement("date")]
        public string Date { get; init; } = string.Empty;

        [BsonElement("type")]
        public int Type { get; init; }

        [BsonElement("sum"), BsonRepresentation(BsonType.Int64)]
        public long Sum { get; init; }
    }

    [BsonIgnoreExtraElements]
    public sealed class QueryDto
    {
        [BsonElement("operationtype")]
        public int OperationType { get; init; }

        [BsonElement("sum"), BsonRepresentation(BsonType.Int64)]
        public long Sum { get; init; }

        [BsonElement("documentid")]
        public long DocumentId { get; init; }

        [BsonElement("fsid")]
        public string FiscMachineId { get; init; } = string.Empty;

        [BsonElement("fiscalsign")]
        public string FiscalSign { get; init; } = string.Empty;

        [BsonElement("date")]
        public string Date { get; init; } = string.Empty;
    }

    [BsonIgnoreExtraElements]
    public sealed class SellerDto
    {
        [BsonElement("name")]
        public string Name { get; init; } = string.Empty;

        [BsonElement("inn")]
        public string Inn { get; init; } = string.Empty;
    }

    [BsonIgnoreExtraElements]
    public sealed class TicketDto
    {
        [BsonElement("document")]
        public TicketDocumentDto? Document { get; init; }
    }

    [BsonIgnoreExtraElements]
    public sealed class TicketDocumentDto
    {
        [BsonElement("receipt")]
        public ReceiptDto? Receipt { get; init; }
    }

    [BsonIgnoreExtraElements]
    public sealed class ReceiptDto
    {
        [BsonElement("datetime")]
        public string Datetime { get; init; } = string.Empty;

        [BsonElement("timestamp"), BsonRepresentation(BsonType.Int64)]
        public long TimestampSeconds { get; init; }

        [BsonElement("cashtotalsum"), BsonRepresentation(BsonType.Int64)]
        public long CashTotalSum { get; init; }

        [BsonElement("code")]
        public int Code { get; init; }

        [BsonElement("creditsum"), BsonRepresentation(BsonType.Int64)]
        public long CreditSum { get; init; }

        [BsonElement("ecashtotalsum"), BsonRepresentation(BsonType.Int64)]
        public long ECashTotalSum { get; init; }

        [BsonElement("fiscaldocumentnumber"), BsonRepresentation(BsonType.Int64)]
        public long FiscalDocumentNumber { get; init; }

        [BsonElement("fiscaldrivenumber")]
        public string FiscalDriveNumber { get; init; } = string.Empty;

        [BsonElement("fiscalsign"), BsonRepresentation(BsonType.Int64)]
        public long FiscalSign { get; init; }

        [BsonElement("fnsurl")]
        public string FnsUrl { get; init; } = string.Empty;

        [BsonElement("items")]
        public List<ReceiptItemDto>? Items { get; init; }

        [BsonElement("kktregid")]
        public string KktRegId { get; init; } = string.Empty;

        [BsonElement("nds10"), BsonRepresentation(BsonType.Int64)]
        public long Nds10 { get; init; }

        [BsonElement("nds18"), BsonRepresentation(BsonType.Int64)]
        public long Nds18 { get; init; }

        [BsonElement("operationtype")]
        public int OperationType { get; init; }

        [BsonElement("operator")]
        public string Operator { get; init; } = string.Empty;

        [BsonElement("prepaidsum"), BsonRepresentation(BsonType.Int64)]
        public long PrepaidSum { get; init; }

        [BsonElement("provisionsum"), BsonRepresentation(BsonType.Int64)]
        public long ProvisionSum { get; init; }

        [BsonElement("requestnumber"), BsonRepresentation(BsonType.Int64)]
        public long RequestNumber { get; init; }

        [BsonElement("retailplace")]
        public string RetailPlace { get; init; } = string.Empty;

        [BsonElement("retailplaceaddress")]
        public string RetailPlaceAddress { get; init; } = string.Empty;

        [BsonElement("shiftnumber"), BsonRepresentation(BsonType.Int64)]
        public long ShiftNumber { get; init; }

        [BsonElement("totalsum"), BsonRepresentation(BsonType.Int64)]
        public long TotalSum { get; init; }

        [BsonElement("user")]
        public string User { get; init; } = string.Empty;

        [BsonElement("userinn")]
        public string UserInn { get; init; } = string.Empty;
    }

    [BsonIgnoreExtraElements]
    public sealed class ReceiptItemDto
    {
        [BsonElement("name")]
        public string Name { get; init; } = string.Empty;

        [BsonElement("nds")]
        public int Nds { get; init; }

        [BsonElement("ndssum"), BsonRepresentation(BsonType.Int64)]
        public long NdsSum { get; init; }

        [BsonElement("paymenttype")]
        public int PaymentType { get; init; }

        [BsonElement("price"), BsonRepresentation(BsonType.Int64)]
        public long Price { get; init; }

        [BsonElement("producttype")]
        public int ProductType { get; init; }

        [BsonElement("quantity"), BsonRepresentation(BsonType.Double)]
        public double Quantity { get; init; }

        [BsonElement("sum"), BsonRepresentation(BsonType.Int64)]
        public long Sum { get; init; }

        [BsonElement("categories")]
        public IReadOnlyList<string>? Categories { get; init; }

        [BsonElement("external_id")]
        public string ExternalId { get; init; } = string.Empty;
    }
}
