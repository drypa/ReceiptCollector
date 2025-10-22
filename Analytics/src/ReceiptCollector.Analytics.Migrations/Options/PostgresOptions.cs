namespace ReceiptCollector.Analytics.Migrations.Options;

internal sealed class PostgresOptions
{
    public const string SectionName = "Infrastructure:Postgres";

    public string? ConnectionString { get; set; }
}
