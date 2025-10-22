namespace ReceiptCollector.Analytics.Migrations.Options;

internal sealed class MigrationScriptsOptions
{
    public const string SectionName = "MigrationScripts";

    public string? DirectoryPath { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;
}
