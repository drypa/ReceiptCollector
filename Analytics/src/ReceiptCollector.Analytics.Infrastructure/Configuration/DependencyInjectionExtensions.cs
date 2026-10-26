using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Users.Options;
using ReceiptCollector.Analytics.Application.Modules.Users.Services;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.AI;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using ReceiptCollector.Analytics.Infrastructure.Synchronization;

namespace ReceiptCollector.Analytics.Infrastructure.Configuration;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.ConfigureInfrastructureOptions(configuration);
        services.AddScoped<ICommodityReadService, CommodityReadService>();
        services.AddScoped<ICommodityRepository, CommodityRepository>();

        // In-memory кэш категорий — Singleton; опция читается при старте (изменение MaxSize требует рестарта).
        services.AddSingleton<ICommodityCategoryCache>(sp => new InMemoryCommodityCategoryCache(
            sp.GetRequiredService<IOptions<CommodityCategoryCacheOptions>>().Value.MaxSize));

        services.AddScoped<ICommodityCategorizationService, CommodityCategorizationService>();
        // ADR-028 п. 9: общий конвейер категоризации (Scoped — SemaphoreSlim на экземпляр, как раньше).
        services.AddScoped<ICommodityCategoryResolver, CommodityCategoryResolver>();
        services.AddScoped<ICommodityBatchCategorizationService, CommodityBatchCategorizationService>();
        services.AddHttpClient<IAiClient, OpenAiCompatibleAiClient>();
        services.AddScoped<IPrecategorizedCommodityRepository, PrecategorizedCommodityRepository>();
        services.AddScoped<IPrecategorizedCommodityService, PrecategorizedCommodityService>();

        // ADR-027 п.9 (NFR-9): единственная точка получения времени/задержек для нового кода.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IApiActivityMonitor, ApiActivityMonitor>();

        services.AddScoped<IReceiptReadService, ReceiptReadService>();
        services.AddScoped<IReceiptRepository, ReceiptRepository>();
        services.AddScoped<IMerchantRepository, MerchantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserAuthLinkRepository, UserAuthLinkRepository>();
        services.AddScoped<IUserAuthLinkService, UserAuthLinkService>();
        services.AddScoped<ReceiptSynchronizationService>();
        services.AddHostedService<ReceiptSynchronizationHostedService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddHostedService<AdminUserHostedService>();
        services.AddHostedService<CommodityCategoryCacheInitializationHostedService>();
        services.AddHostedService<PrecategorizationBackgroundService>();
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

        services.AddOptions<UserAuthLinkOptions>()
            .Bind(configuration.GetSection(UserAuthLinkOptions.SectionName));
            
        services.AddOptions<AdminUserOptions>()
            .Bind(configuration.GetSection(AdminUserOptions.SectionName));

        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName));

        services.AddOptions<CommodityCategoryCacheOptions>()
            .Bind(configuration.GetSection(CommodityCategoryCacheOptions.SectionName));

        // ADR-028 п.3: бюджет одного suggest-запроса страницы. Невалидное значение — ошибка старта
        // (как у Precategorization, NFR-7), а не «тихая» деградация к таймауту прокси.
        services.AddOptions<CommodityBatchCategorizationOptions>()
            .Bind(configuration.GetSection(CommodityBatchCategorizationOptions.SectionName))
            .Validate(o => o.SuggestTimeout > TimeSpan.Zero, "CommodityBatchCategorization:SuggestTimeout must be greater than zero.")
            .ValidateOnStart();

        // NFR-7: невалидные значения — явная ошибка на старте (OptionsValidationException),
        // а не «тихая деградация» фона.
        services.AddOptions<PrecategorizationOptions>()
            .Bind(configuration.GetSection(PrecategorizationOptions.SectionName))
            .Validate(o => o.IdleTimeout > TimeSpan.Zero, "Precategorization:IdleTimeout must be greater than zero.")
            .Validate(o => o.IdleCheckInterval > TimeSpan.Zero, "Precategorization:IdleCheckInterval must be greater than zero.")
            .Validate(o => o.BatchLimit >= 1, "Precategorization:BatchLimit must be at least 1.")
            .Validate(o => o.IntervalBetweenCalls >= TimeSpan.Zero, "Precategorization:IntervalBetweenCalls cannot be negative.")
            // ADR-030: окно задаётся обеими границами или не задаётся вовсе; границы — время суток.
            .Validate(
                o => o.WorkWindowStart.HasValue == o.WorkWindowEnd.HasValue,
                "Precategorization:WorkWindowStart and Precategorization:WorkWindowEnd must be set together (or both omitted).")
            .Validate(
                o => o.WorkWindowStart is not { } start || (start >= TimeSpan.Zero && start < TimeSpan.FromDays(1)),
                "Precategorization:WorkWindowStart must be within [00:00:00, 24:00:00).")
            .Validate(
                o => o.WorkWindowEnd is not { } end || (end >= TimeSpan.Zero && end < TimeSpan.FromDays(1)),
                "Precategorization:WorkWindowEnd must be within [00:00:00, 24:00:00).")
            .Validate(
                o => !o.WorkWindowStart.HasValue || !o.WorkWindowEnd.HasValue || o.WorkWindowStart != o.WorkWindowEnd,
                "Precategorization:WorkWindowStart and Precategorization:WorkWindowEnd must differ (omit both to run around the clock).")
            .Validate(
                o => IsValidTimeZoneId(o.WorkWindowTimeZone),
                "Precategorization:WorkWindowTimeZone must be a valid IANA or Windows time zone id.")
            .ValidateOnStart();

        services.AddDbContext<ReceiptDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException("Postgres connection string is not configured.");
            }

            builder
                .UseNpgsql(options.ConnectionString)
                .UseSnakeCaseNamingConvention();
        });

        services.AddSingleton<IMongoReceiptBatchLoader, MongoReceiptBatchLoader>();
        services.AddSingleton<IMongoUserLoader, MongoUserLoader>();
        // D3: resolver и loader владельца — singleton (кэш ConcurrentDictionary переживает циклы синхронизации).
        services.AddSingleton<IMongoReceiptRequestLoader, MongoReceiptRequestLoader>();
        services.AddSingleton<IReceiptOwnerResolver, ReceiptOwnerResolver>();
    }

    /// <summary>
    /// Пустой id допустим (локальное время сервера); непустой должен разрешаться
    /// <see cref="TimeZoneInfo"/> — иначе ошибка старта (ADR-030).
    /// </summary>
    private static bool IsValidTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return true;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}

