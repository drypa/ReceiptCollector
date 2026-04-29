using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal sealed class AdminUserHostedService : IHostedService
{
    private readonly ILogger<AdminUserHostedService> _logger;
    private readonly IAdminUserService _adminUserService;

    public AdminUserHostedService(
        ILogger<AdminUserHostedService> logger,
        IAdminUserService adminUserService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adminUserService = adminUserService ?? throw new ArgumentNullException(nameof(adminUserService));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Admin User Hosted Service");
        
        try
        {
            await _adminUserService.UpdateAdminStatusAsync(cancellationToken);
            
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