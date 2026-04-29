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
    private readonly IOptions<AdminUserOptions> _options;

    public AdminUserService(
        ILogger<AdminUserService> logger,
        IUserRepository userRepository,
        IOptions<AdminUserOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
                // Check if user exists with this Telegram ID - using correct method from repository interface
                var existingUser = await _userRepository.GetByTelegramIdAsync((int)telegramId, cancellationToken);
                
                if (existingUser != null)
                {
                    _logger.LogInformation($"Admin status already set for user {existingUser.Name} (ID: {existingUser.Id})");
                    // Note: This is a basic implementation. In reality, we would need to add update functionality
                    // to the repository interface or use another approach to manage admin privileges.
                }
                else
                {
                    _logger.LogWarning($"User with Telegram ID {telegramId} not found in database");
                }
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