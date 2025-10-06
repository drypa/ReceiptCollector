using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

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
        // Зарезервировано для будущей конфигурации источников данных.
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
