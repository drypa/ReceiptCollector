using MongoDB.Bson;

namespace ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

/// <summary>
/// Обёртка над BsonDocument из коллекции <c>raw_tickets</c>.
/// Документ перед обёрткой нормализуется (<see cref="Normalize"/>) — ключи приводятся к
/// нижнему регистру + применяется alias-словарь (D2), поэтому все аксессоры используют
/// lowercase-ключи. Формат исходного документа не гарантирован (lowercase при записи
/// драйвером mongo-go v1.17.9 либо camelCase) — нормализация покрывает оба варианта.
/// </summary>
public sealed class RawTicketDocument
{
    /// <summary>
    /// Aliases ключей, которые не получаются простым приведением к нижнему регистру.
    /// Поле Nds18 в nalogru.TicketDetails имеет json-тег "nd18"; на хранение тег не
    /// влияет, но подстраховываемся на случай документов, сериализованных по json-тегам.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KeyAliases =
        new Dictionary<string, string>
        {
            ["nd18"] = "nds18"
        };

    private readonly BsonDocument _document;

    private RawTicketDocument(BsonDocument document)
    {
        _document = document;
    }

    public ObjectId MongoId => _document.TryGetValue("_id", out var id) && id.IsObjectId
        ? id.AsObjectId
        : ObjectId.Empty;

    /// <summary>Внешний id тикета (UUID из ключа <c>id</c>); отсутствует — null (D8: fallback на _id в маппере).</summary>
    public string? ExternalId => GetTopLevelString("id");

    public string? Qr => GetTopLevelString("qr");

    /// <summary>Нормализованный query_string тикета (может отсутствовать в raw_tickets).</summary>
    public string? QueryString => GetTopLevelString("query_string");

    /// <summary>Верхнеуровневый owner (обычно отсутствует в raw_tickets — резолвится через receipt_requests, D3).</summary>
    public string? Owner => GetOwnerString();

    public int Status => _document.TryGetValue("status", out var status) && status.IsNumeric
        ? status.ToInt32()
        : 0;

    public string? SellerName => GetNestedString("seller", "name");

    public string? SellerInn => GetNestedString("seller", "inn");

    /// <summary>
    /// Payload чека: вложенный <c>ticket.document.receipt</c> ИЛИ верхнеуровневый
    /// <c>receipt</c> (legacy-формат 2020). null — документ «не fulfilled» (D2, D8).
    /// </summary>
    public ReceiptPayload? GetPayload()
    {
        var nested = GetNestedDocument("ticket", "document", "receipt");
        var payload = nested ?? GetNestedDocument("receipt");
        return payload is null ? null : new ReceiptPayload(payload);
    }

    /// <summary>
    /// Дата покупки по D5, строго в порядке приоритета:
    /// 1. параметр <c>t=YYYYMMDDTHHMM</c> из QR/query_string (локальное время, одинаково
    ///    для legacy и нового формата — канонический источник для natural key);
    /// 2. <c>receipt.datetime</c> как число (int64 unix-секунды) → UTC;
    /// 3. <c>receipt.datetime</c> как строка → TryParse;
    /// 4. ничего нет → null (документ пропускается сервисом с логом).
    /// Результат всегда нормализуется в UTC (как ReceiptEntity.NormalizeUtc).
    /// </summary>
    public DateTime? GetPurchasedAt()
    {
        // Шаг 1 (D5): время из QR — единственный источник, детерминированный для обоих форматов.
        var qrTimeParameter = ExtractQueryParameter(Qr, "t") ?? ExtractQueryParameter(QueryString, "t");
        if (TryParseQrTime(qrTimeParameter, out var qrDateTime))
        {
            return NormalizeUtc(qrDateTime);
        }

        var payload = GetPayload();

        // Шаг 2 (D5): unix-секунды (новый формат).
        var dateTimeValue = payload?.DateTimeValue;
        if (dateTimeValue is not null)
        {
            if (dateTimeValue.IsNumeric && dateTimeValue.IsInt64)
            {
                return DateTimeOffset.FromUnixTimeSeconds(dateTimeValue.ToInt64()).UtcDateTime;
            }

            // Шаг 3 (D5): legacy-строка "yyyy-MM-ddTHH:mm:ss".
            if (dateTimeValue.IsString &&
                DateTime.TryParse(dateTimeValue.AsString, out var parsedDate))
            {
                return NormalizeUtc(parsedDate);
            }
        }

        // Шаг 4: данных о дате нет — сервис пропустит документ.
        return null;
    }

    /// <summary>
    /// Рекурсивная нормализация ключей документа: к нижнему регистру + alias-словарь (D2).
    /// </summary>
    public static BsonDocument Normalize(BsonDocument source)
    {
        var result = new BsonDocument();
        foreach (var element in source)
        {
            var key = element.Name.ToLowerInvariant();
            if (KeyAliases.TryGetValue(key, out var aliasKey))
            {
                key = aliasKey;
            }

            result[key] = NormalizeValue(element.Value);
        }

        return result;
    }

    public static RawTicketDocument FromBsonDocument(BsonDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RawTicketDocument(Normalize(source));
    }

    private static BsonValue NormalizeValue(BsonValue value)
    {
        if (value.IsBsonDocument)
        {
            return Normalize(value.AsBsonDocument);
        }

        if (value.IsBsonArray)
        {
            return new BsonArray(value.AsBsonArray.Select(NormalizeValue));
        }

        return value;
    }

    private string? GetTopLevelString(string key)
    {
        return _document.TryGetValue(key, out var value) && value.IsString
            ? value.AsString
            : null;
    }

    private string? GetOwnerString()
    {
        if (!_document.TryGetValue("owner", out var owner))
        {
            return null;
        }

        if (owner.IsObjectId)
        {
            return owner.AsObjectId.ToString();
        }

        return owner.IsString ? owner.AsString : null;
    }

    private BsonDocument? GetNestedDocument(params string[] path)
    {
        BsonValue? current = _document;
        foreach (var key in path)
        {
            if (current is not BsonDocument currentDocument ||
                !currentDocument.TryGetValue(key, out var next))
            {
                return null;
            }

            current = next;
        }

        return current as BsonDocument;
    }

    private string? GetNestedString(params string[] path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        var parentPath = path[..^1];
        var document = parentPath.Length == 0 ? _document : GetNestedDocument(parentPath);
        return document is null ? null : GetTopLevelString(document, path[^1]);
    }

    private static string? GetTopLevelString(BsonDocument document, string key)
    {
        return document.TryGetValue(key, out var value) && value.IsString
            ? value.AsString
            : null;
    }

    private static string? ExtractQueryParameter(string? query, string name)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var pair in query.Split('&'))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = pair[..separatorIndex];
            var value = pair[(separatorIndex + 1)..];
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(value);
            }
        }

        return null;
    }

    private static bool TryParseQrTime(string? value, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Формат из backend/nalogru/qr/query.go: t=YYYYMMDDTHHMM (локальное время, точность — минута).
        return DateTime.TryParseExact(
            value,
            "yyyyMMdd'T'HHmm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out result);
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}

/// <summary>
/// Payload чека (ticket.document.receipt либо верхнеуровневый receipt).
/// Значения читаются напрямую из BsonValue (число/строка) — защита от mixed-типа
/// datetime без строготипизированного deserializer (D2).
/// </summary>
public sealed class ReceiptPayload
{
    private readonly BsonDocument _document;

    public ReceiptPayload(BsonDocument document)
    {
        _document = document;
    }

    /// <summary>datetime чека: int64 unix-секунды (новый формат) либо строка (legacy).</summary>
    public BsonValue? DateTimeValue => GetValue("datetime");

    public long? TotalSumMinor => GetInt64("totalsum");

    public string? UserInn => GetString("userinn");

    public string? User => GetString("user");

    public string? Operator => GetString("operator");

    public string? RetailPlaceAddress => GetString("retailplaceaddress");

    /// <summary>Сумма НДС 18% (ключ нормализован: nd18 → nds18).</summary>
    public long? Nds18Minor => GetInt64("nds18");

    public IReadOnlyList<ReceiptItemPayload> Items
    {
        get
        {
            if (!_document.TryGetValue("items", out var items) || !items.IsBsonArray)
            {
                return Array.Empty<ReceiptItemPayload>();
            }

            return items.AsBsonArray
                .Where(item => item.IsBsonDocument)
                .Select(item => new ReceiptItemPayload(item.AsBsonDocument))
                .ToArray();
        }
    }

    private BsonValue? GetValue(string key)
    {
        return _document.TryGetValue(key, out var value) ? value : null;
    }

    private string? GetString(string key)
    {
        var value = GetValue(key);
        return value is not null && value.IsString ? value.AsString : null;
    }

    private long? GetInt64(string key)
    {
        var value = GetValue(key);
        return value is not null && value.IsNumeric ? value.ToInt64() : null;
    }
}

/// <summary>Элемент чека (позиция).</summary>
public sealed class ReceiptItemPayload
{
    private readonly BsonDocument _document;

    public ReceiptItemPayload(BsonDocument document)
    {
        _document = document;
    }

    public string? Name => GetString("name");

    public double? QuantityRaw => GetDouble("quantity");

    public long? PriceMinor => GetInt64("price");

    public long? SumMinor => GetInt64("sum");

    public int? Nds => ToInt32(GetInt64("nds"));

    public long? NdsSumMinor => GetInt64("ndssum");

    public int? PaymentType => ToInt32(GetInt64("paymenttype"));

    public int? ProductType => ToInt32(GetInt64("producttype"));

    private string? GetString(string key)
    {
        return _document.TryGetValue(key, out var value) && value.IsString
            ? value.AsString
            : null;
    }

    private double? GetDouble(string key)
    {
        return _document.TryGetValue(key, out var value) && value.IsNumeric
            ? value.ToDouble()
            : null;
    }

    private long? GetInt64(string key)
    {
        return _document.TryGetValue(key, out var value) && value.IsNumeric
            ? value.ToInt64()
            : null;
    }

    private static int? ToInt32(long? value) => value is null ? null : (int)value.Value;
}