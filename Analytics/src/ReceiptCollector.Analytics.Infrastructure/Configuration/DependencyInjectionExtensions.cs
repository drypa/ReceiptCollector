using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;

namespace ReceiptCollector.Analytics.Infrastructure.Configuration;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureInfrastructureOptions(configuration);
        services.AddScoped<IReceiptReadService, StubReceiptReadService>();
        return services;
    }

    private static void ConfigureInfrastructureOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoReceiptSourceOptions>()
            .Bind(configuration.GetSection(MongoReceiptSourceOptions.SectionName));

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
