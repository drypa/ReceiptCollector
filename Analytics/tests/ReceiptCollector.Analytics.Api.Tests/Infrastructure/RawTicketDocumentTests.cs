using MongoDB.Bson;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

public sealed class RawTicketDocumentTests
{
    private const string QrTime = "t=20191005T1548&s=1127.00&fn=9282000100254567&i=11401&fp=371532793&n=1";

    [Fact]
    public void GetPurchasedAt_uses_qr_time_when_present()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["qr"] = QrTime,
            ["ticket"] = new BsonDocument
            {
                ["document"] = new BsonDocument
                {
                    ["receipt"] = new BsonDocument
                    {
                        ["datetime"] = BsonInt64.Create(1780116075),
                        ["totalsum"] = BsonInt64.Create(112700)
                    }
                }
            }
        });

        var purchasedAt = Assert.IsType<DateTime>(document.GetPurchasedAt());

        // Время покупки из QR — локальное "2019-10-05 15:48", нормализуется в UTC без сдвига.
        Assert.Equal(new DateTime(2019, 10, 5, 15, 48, 0, DateTimeKind.Utc), purchasedAt);
    }

    [Fact]
    public void GetPurchasedAt_uses_qr_time_from_query_string_when_qr_is_missing()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["query_string"] = QrTime
        });

        var purchasedAt = Assert.IsType<DateTime>(document.GetPurchasedAt());

        Assert.Equal(new DateTime(2019, 10, 5, 15, 48, 0, DateTimeKind.Utc), purchasedAt);
    }

    [Fact]
    public void GetPurchasedAt_parses_datetime_as_unix_seconds_when_qr_is_missing()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["ticket"] = new BsonDocument
            {
                ["document"] = new BsonDocument
                {
                    ["receipt"] = new BsonDocument
                    {
                        ["datetime"] = BsonInt64.Create(1570280880)
                    }
                }
            }
        });

        var purchasedAt = Assert.IsType<DateTime>(document.GetPurchasedAt());

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1570280880).UtcDateTime, purchasedAt);
    }

    [Fact]
    public void GetPurchasedAt_parses_datetime_as_string_when_qr_is_missing()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["receipt"] = new BsonDocument
            {
                ["datetime"] = "2019-10-05T15:48:00"
            }
        });

        var purchasedAt = Assert.IsType<DateTime>(document.GetPurchasedAt());

        Assert.Equal(new DateTime(2019, 10, 5, 15, 48, 0, DateTimeKind.Utc), purchasedAt);
    }

    [Fact]
    public void GetPurchasedAt_returns_null_when_no_timestamp_available()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["ticket"] = new BsonDocument
            {
                ["document"] = new BsonDocument
                {
                    ["receipt"] = new BsonDocument
                    {
                        ["totalsum"] = BsonInt64.Create(112700)
                    }
                }
            }
        });

        Assert.Null(document.GetPurchasedAt());
    }

    [Fact]
    public void GetPayload_returns_payload_from_ticket_document_receipt()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["ticket"] = new BsonDocument
            {
                ["document"] = new BsonDocument
                {
                    ["receipt"] = new BsonDocument
                    {
                        ["datetime"] = BsonInt64.Create(1570280880),
                        ["totalsum"] = BsonInt64.Create(112700),
                        ["userinn"] = "5003042456",
                        ["user"] = "ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"",
                        ["operator"] = "18 ИВАНОВА",
                        ["retailplaceaddress"] = "117556 г. Москва, Варшавское шоссе, 97",
                        ["items"] = new BsonArray(new[]
                        {
                            new BsonDocument
                            {
                                ["name"] = "ПАНЕЛЬ  250Х3000",
                                ["quantity"] = 6.0,
                                ["price"] = BsonInt64.Create(14800),
                                ["sum"] = BsonInt64.Create(88800),
                                ["nds"] = 20,
                                ["ndssum"] = BsonInt64.Create(14800),
                                ["paymenttype"] = 4,
                                ["producttype"] = 1
                            }
                        })
                    }
                }
            }
        });

        var payload = document.GetPayload();

        Assert.NotNull(payload);
        Assert.Equal(112700, payload!.TotalSumMinor);
        Assert.Equal("5003042456", payload.UserInn);
        Assert.Equal("ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"", payload.User);
        Assert.Equal("18 ИВАНОВА", payload.Operator);
        Assert.Equal("117556 г. Москва, Варшавское шоссе, 97", payload.RetailPlaceAddress);
        var item = Assert.Single(payload.Items);
        Assert.Equal("ПАНЕЛЬ  250Х3000", item.Name);
        Assert.Equal(6, item.QuantityRaw);
        Assert.Equal(14800, item.PriceMinor);
        Assert.Equal(88800, item.SumMinor);
        Assert.Equal(20, item.Nds);
        Assert.Equal(14800, item.NdsSumMinor);
        Assert.Equal(4, item.PaymentType);
        Assert.Equal(1, item.ProductType);
    }

    [Fact]
    public void GetPayload_returns_payload_from_top_level_receipt_for_legacy_format()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["receipt"] = new BsonDocument
            {
                ["datetime"] = "2019-10-05T15:48:00",
                ["totalsum"] = BsonInt64.Create(112700),
                ["userinn"] = "5003042456"
            }
        });

        var payload = document.GetPayload();

        Assert.NotNull(payload);
        Assert.Equal(112700, payload!.TotalSumMinor);
        Assert.Equal("5003042456", payload.UserInn);
        Assert.True(payload.DateTimeValue!.IsString);
    }

    [Fact]
    public void GetPayload_returns_null_when_document_is_not_fulfilled()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["status"] = 1,
            ["ticket"] = BsonNull.Value
        });

        Assert.Null(document.GetPayload());
    }

    [Fact]
    public void Normalize_lowercases_keys_recursively()
    {
        var normalized = RawTicketDocument.Normalize(new BsonDocument
        {
            ["Id"] = "3f2b4a9c-8e27-4a1b-9c55-6f7a1b2c3d4e",
            ["Ticket"] = new BsonDocument
            {
                ["Document"] = new BsonDocument
                {
                    ["Receipt"] = new BsonDocument
                    {
                        ["TotalSum"] = BsonInt64.Create(112700)
                    }
                }
            }
        });

        Assert.True(normalized.Contains("id"));
        Assert.False(normalized.Contains("Id"));
        Assert.True(normalized.Contains("ticket"));
        var receipt = normalized["ticket"]["document"]["receipt"].AsBsonDocument;
        Assert.True(receipt.Contains("totalsum"));
        Assert.False(receipt.Contains("TotalSum"));
    }

    [Fact]
    public void Normalize_applies_nd18_alias_to_nds18()
    {
        var normalized = RawTicketDocument.Normalize(new BsonDocument
        {
            ["ticket"] = new BsonDocument
            {
                ["document"] = new BsonDocument
                {
                    ["receipt"] = new BsonDocument
                    {
                        ["nd18"] = BsonInt64.Create(18783)
                    }
                }
            }
        });

        var receipt = normalized["ticket"]["document"]["receipt"].AsBsonDocument;
        Assert.False(receipt.Contains("nd18"));
        Assert.True(receipt.Contains("nds18"));
        Assert.Equal(18783, receipt["nds18"].AsInt64);

        var payload = RawTicketDocument.FromBsonDocument(normalized).GetPayload();
        Assert.Equal(18783, payload!.Nds18Minor);
    }

    [Fact]
    public void Normalize_lowercases_keys_in_nested_arrays()
    {
        var normalized = RawTicketDocument.Normalize(new BsonDocument
        {
            ["ticket"] = new BsonDocument
            {
                ["document"] = new BsonDocument
                {
                    ["receipt"] = new BsonDocument
                    {
                        ["items"] = new BsonArray(new[]
                        {
                            new BsonDocument
                            {
                                ["Name"] = "ПАНЕЛЬ",
                                ["NdsSum"] = BsonInt64.Create(14800)
                            }
                        })
                    }
                }
            }
        });

        var items = normalized["ticket"]["document"]["receipt"]["items"].AsBsonArray;
        var item = items[0].AsBsonDocument;
        Assert.True(item.Contains("name"));
        Assert.True(item.Contains("ndssum"));
        Assert.False(item.Contains("NdsSum"));
    }

    [Fact]
    public void ExternalId_reads_ticket_id_key()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["id"] = "3f2b4a9c-8e27-4a1b-9c55-6f7a1b2c3d4e",
            ["_id"] = ObjectId.Parse("66f0aa000000000000000001")
        });

        Assert.Equal("3f2b4a9c-8e27-4a1b-9c55-6f7a1b2c3d4e", document.ExternalId);
        Assert.Equal(ObjectId.Parse("66f0aa000000000000000001"), document.MongoId);
    }

    [Fact]
    public void ExternalId_is_null_when_id_key_is_missing()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["_id"] = ObjectId.Parse("66f0aa000000000000000001")
        });

        Assert.Null(document.ExternalId);
        Assert.Equal(ObjectId.Parse("66f0aa000000000000000001"), document.MongoId);
    }

    [Fact]
    public void Seller_accessors_read_name_and_inn()
    {
        var document = RawTicketDocument.FromBsonDocument(new BsonDocument
        {
            ["seller"] = new BsonDocument
            {
                ["name"] = "ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"",
                ["inn"] = "5003042456"
            }
        });

        Assert.Equal("ООО \"СДЕЛАЙ СВОИМИ РУКАМИ\"", document.SellerName);
        Assert.Equal("5003042456", document.SellerInn);
    }
}