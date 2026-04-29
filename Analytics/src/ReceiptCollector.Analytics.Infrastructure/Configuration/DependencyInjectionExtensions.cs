using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Users.Services;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.DataSources.Mongo;
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
    }
}

