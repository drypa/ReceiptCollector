using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;

namespace ReceiptCollector.Analytics.Api.Tests;

public class UserAuthEndpointsTests
{
    [Fact]
    public async Task RequestAuthLink_returns_bad_request_when_telegram_id_missing()
    {
        var service = new FakeUserAuthLinkService();

        var result = await InvokeRequestAuthLinkAsync(null, service);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("telegramId is required.", badRequest.Value);
    }

    [Fact]
    public async Task RequestAuthLink_returns_not_found_when_user_missing()
    {
        var service = new FakeUserAuthLinkService
        {
            ShouldThrowForTelegramId = 123
        };

        var result = await InvokeRequestAuthLinkAsync(123, service);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task RequestAuthLink_returns_link_when_user_found()
    {
        var expectedLink = new UserAuthLinkResult("https://link", DateTimeOffset.UtcNow.AddMinutes(5));
        var service = new FakeUserAuthLinkService
        {
            GeneratedResult = expectedLink
        };

        var result = await InvokeRequestAuthLinkAsync(123, service);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

        var value = ok.Value!;
        var valueType = value.GetType();
        var linkProperty = valueType.GetProperty("Link", BindingFlags.Public | BindingFlags.Instance);
        var expiresAtProperty = valueType.GetProperty("ExpiresAt", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(linkProperty);
        Assert.NotNull(expiresAtProperty);

        Assert.Equal(expectedLink.Link, (string?)linkProperty!.GetValue(value));
        Assert.Equal(expectedLink.ExpiresAt, (DateTimeOffset?)expiresAtProperty!.GetValue(value));
    }

    private static Task<IResult> InvokeRequestAuthLinkAsync(
        int? telegramId,
        IUserAuthLinkService service)
    {
        var method = typeof(UserAuthEndpoints).GetMethod(
            "RequestAuthLink",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var task = (Task<IResult>)method.Invoke(null, new object?[]
        {
            telegramId,
            service,
            CancellationToken.None
        })!;

        return task;
    }

    private sealed class FakeUserAuthLinkService : IUserAuthLinkService
    {
        public int? ShouldThrowForTelegramId { get; init; }

        public UserAuthLinkResult GeneratedResult { get; init; } = new("https://default", DateTimeOffset.UtcNow);

        public Task<UserAuthLinkResult> GenerateAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(GeneratedResult);

        public Task<UserAuthLinkResult> GenerateByTelegramIdAsync(int telegramId, CancellationToken cancellationToken)
        {
            if (ShouldThrowForTelegramId == telegramId)
            {
                throw new InvalidOperationException("Not found");
            }

            return Task.FromResult(GeneratedResult);
        }

        public Task<UserAuthLinkValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

}
