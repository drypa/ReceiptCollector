using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Users.Options;

namespace ReceiptCollector.Analytics.Application.Modules.Users.Services;

public sealed class AdminUserService : IAdminUserService
{
    private readonly ILogger<AdminUserService> _logger;
    private readonly IUserRepository _userRepository;
    private readonly AdminUserOptions _adminUserOptions;

    public AdminUserService(
        ILogger<AdminUserService> logger,
        IUserRepository userRepository,
        IOptions<AdminUserOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        ArgumentNullException.ThrowIfNull(options);
        _adminUserOptions = options.Value;
    }

    public async Task UpdateAdminStatusAsync(CancellationToken cancellationToken)
    {
        if (_adminUserOptions.TelegramIds == null || !_adminUserOptions.TelegramIds.Any())
        {
            _logger.LogInformation("No admin users configured in the settings");
            return;
        }
        
        _logger.LogInformation($"Updating admin status for {string.Join(", ", _adminUserOptions.TelegramIds)}");

        foreach (var telegramId in _adminUserOptions.TelegramIds)
        {
            try
            {
                await _userRepository.UpdateAdminStatusAsync(telegramId, true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating admin status for user with Telegram ID: {telegramId}");
                throw;
            }
        }

        _logger.LogInformation("Admin status update completed successfully");
    }
}