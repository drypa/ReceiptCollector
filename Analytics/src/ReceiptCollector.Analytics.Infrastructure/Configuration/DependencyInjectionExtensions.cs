using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using ReceiptCollector.Analytics.Infrastructure.Synchronization;

namespace ReceiptCollector.Analytics.Infrastructure.Configuration;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureInfrastructureOptions(configuration);
        services.AddScoped<IReceiptReadService, StubReceiptReadService>();
        services.AddScoped<IReceiptRepository, ReceiptRepository>();
        services.AddScoped<ReceiptSynchronizationService>();
        services.AddHostedService<ReceiptSynchronizationHostedService>();
        return services;
    }

    private static void ConfigureInfrastructureOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoReceiptSourceOptions>()
            .Bind(configuration.GetSection(MongoReceiptSourceOptions.SectionName));

        services.AddOptions<PostgresOptions>()
            .Bind(configuration.GetSection(PostgresOptions.SectionName));

        services.AddOptions<ReceiptSynchronizationOptions>()
            .Bind(configuration.GetSection(ReceiptSynchronizationOptions.SectionName));

        services.AddDbContext<ReceiptDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException("Postgres connection string is not configured.");
            }

            builder.UseNpgsql(options.ConnectionString);
        });

        services.AddSingleton<IMongoReceiptBatchLoader, MongoReceiptBatchLoader>();
    }
}

internal sealed class StubReceiptReadService : IReceiptReadService
{
    public Task<IReadOnlyCollection<ReceiptSummaryDto>> GetRecentAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ReceiptSummaryDto> result = Array.Empty<ReceiptSummaryDto>();
        return Task.FromResult(result);
    }

    public Task<ReceiptDetailsDto?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        return Task.FromResult<ReceiptDetailsDto?>(null);
    }
}
