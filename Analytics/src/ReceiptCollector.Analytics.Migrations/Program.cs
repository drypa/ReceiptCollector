using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Migrations;
using ReceiptCollector.Analytics.Migrations.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "RECEIPTCOLLECTOR_")
    .AddCommandLine(args);

builder.Services.AddOptions<PostgresOptions>()
    .Bind(builder.Configuration.GetSection(PostgresOptions.SectionName));

builder.Services.AddOptions<MigrationScriptsOptions>()
    .Bind(builder.Configuration.GetSection(MigrationScriptsOptions.SectionName));

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

builder.Services.AddSingleton<MigrationRunner>();

using var host = builder.Build();

var runner = host.Services.GetRequiredService<MigrationRunner>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

await runner.RunAsync(cts.Token).ConfigureAwait(false);
