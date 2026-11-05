using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Wiring-тест DI для зависимости <c>CommodityRepository</c> от in-memory кэша категорий
/// (T-04, ADR-032 F1). Кэш внедряется параметром со значением по умолчанию (совместимость
/// существующих тестовых фабрик репозитория — прецедент T-03), поэтому потеря регистрации
/// <c>ICommodityCategoryCache</c> в <see cref="DependencyInjectionExtensions"/> не сломала бы
/// ни unit-тесты, ни запись в БД — пополнение кэша при применении дефолта просто «молча»
/// отключилось бы в production. Проверка через reflect закрытого поля ловит это.
/// </summary>
public sealed class CommodityRepositoryCacheWiringTests
{
    private static ServiceProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Подключение не открывается — важно лишь, что DbContext конфигурируется.
                ["Infrastructure:Postgres:ConnectionString"] = "Host=localhost;Database=wiring;Username=u;Password=p",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Container_injects_commodity_category_cache_into_commodity_repository()
    {
        using var provider = CreateProvider();

        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICommodityRepository>();

        var cacheField = typeof(CommodityRepository)
            .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(cacheField);
        Assert.NotNull(cacheField!.GetValue(repository));
    }
}
