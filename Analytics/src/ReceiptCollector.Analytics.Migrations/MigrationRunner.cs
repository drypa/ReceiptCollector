using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using ReceiptCollector.Analytics.Migrations.Options;

namespace ReceiptCollector.Analytics.Migrations;

internal sealed class MigrationRunner
{
    private readonly ILogger<MigrationRunner> _logger;
    private readonly PostgresOptions _postgresOptions;
    private readonly MigrationScriptsOptions _scriptsOptions;

    public MigrationRunner(
        ILogger<MigrationRunner> logger,
        IOptions<PostgresOptions> postgresOptions,
        IOptions<MigrationScriptsOptions> scriptsOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _postgresOptions = postgresOptions?.Value ?? throw new ArgumentNullException(nameof(postgresOptions));
        _scriptsOptions = scriptsOptions?.Value ?? throw new ArgumentNullException(nameof(scriptsOptions));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var connectionString = _postgresOptions.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Postgres connection string is not configured.");
        }

        var scriptsDirectory = _scriptsOptions.DirectoryPath;

        if (string.IsNullOrWhiteSpace(scriptsDirectory))
        {
            throw new InvalidOperationException("Scripts directory is not configured.");
        }

        var directory = new DirectoryInfo(Path.GetFullPath(scriptsDirectory));

        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Scripts directory '{directory.FullName}' was not found.");
        }

        var scriptFiles = directory
            .GetFiles("*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();

        if (scriptFiles.Count == 0)
        {
            _logger.LogInformation("No SQL scripts found in directory '{Directory}'.", directory.FullName);
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Connected to Postgres instance '{DataSource}'.", connection.DataSource);

        await EnsureHistoryTableAsync(connection, cancellationToken).ConfigureAwait(false);

        var appliedScripts = await LoadAppliedScriptsAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var script in scriptFiles)
        {
            if (appliedScripts.Contains(script.Name))
            {
                _logger.LogInformation("Skipping already applied script {ScriptName}.", script.Name);
                continue;
            }

            _logger.LogInformation("Applying script {ScriptName}...", script.Name);

            var scriptContent = await File.ReadAllTextAsync(script.FullName, cancellationToken).ConfigureAwait(false);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await ExecuteScriptAsync(connection, transaction, scriptContent, cancellationToken).ConfigureAwait(false);
                await RecordScriptAsync(connection, transaction, script.Name, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Script {ScriptName} applied successfully.", script.Name);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError(ex, "Failed to apply script {ScriptName}. Transaction rolled back.", script.Name);
                throw;
            }
        }

        _logger.LogInformation("All migrations are up to date.");
    }

    private async Task ExecuteScriptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string scriptContent, CancellationToken cancellationToken)
    {
        var timeout = _scriptsOptions.CommandTimeoutSeconds;

        await using var command = new NpgsqlCommand(scriptContent, connection, transaction)
        {
            CommandTimeout = timeout > 0 ? timeout : 0
        };

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureHistoryTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS migration_scripts_history
            (
                script_name text PRIMARY KEY,
                applied_on timestamptz NOT NULL DEFAULT now()
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImmutableHashSet<string>> LoadAppliedScriptsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "SELECT script_name FROM migration_scripts_history";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(reader.GetString(0));
        }

        return builder.ToImmutable();
    }

    private static async Task RecordScriptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string scriptName, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO migration_scripts_history (script_name, applied_on)
            VALUES (@name, now());
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("name", scriptName);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
