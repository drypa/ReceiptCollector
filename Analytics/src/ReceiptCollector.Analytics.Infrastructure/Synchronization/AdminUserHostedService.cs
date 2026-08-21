using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal sealed class AdminUserHostedService : IHostedService
{
    private readonly ILogger<AdminUserHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public AdminUserHostedService(
        ILogger<AdminUserHostedService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Admin User Hosted Service");
        
        try
        {
            // Create a scope to resolve the scoped service
            using var scope = _serviceProvider.CreateScope();
            var adminUserService = scope.ServiceProvider.GetRequiredService<IAdminUserService>();
            await adminUserService.UpdateAdminStatusAsync(cancellationToken);
            
            _logger.LogInformation("Admin User Hosted Service completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Admin User Hosted Service execution");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}