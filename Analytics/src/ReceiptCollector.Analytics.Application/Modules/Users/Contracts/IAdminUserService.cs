namespace ReceiptCollector.Analytics.Application.Modules.Users.Contracts;

public interface IAdminUserService
{
    Task UpdateAdminStatusAsync(CancellationToken cancellationToken);
}