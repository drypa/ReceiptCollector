using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Receipts;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Tests;

public class MerchantEndpointsTests
{
    private static readonly Merchant? MerchantNotFound = null;

    [Fact]
    public async Task UpdateMerchantName_WithAdminUser_ShouldUpdateSuccessfully()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var existingMerchant = new Merchant(merchantId, "Old Name", MerchantCategory.Undefined);
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(existingMerchant);
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await ReceiptEndpoints.UpdateMerchantName(
            merchantId,
            new UpdateMerchantNameRequest("New Name"),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.Received(1).AddAsync(
            Arg.Is<Merchant>(m => m.Name == "New Name"),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>(result);
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<string>)result;
        Assert.Equal("Merchant name updated successfully.", okResult.Value);
    }

    [Fact]
    public async Task UpdateMerchantName_WithNonAdminUser_ShouldReturnForbidden()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var user = new User(userId, "userName", "12345", 111222, isAdmin: false);

        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await ReceiptEndpoints.UpdateMerchantName(
            merchantId,
            new UpdateMerchantNameRequest("New Name"),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().AddAsync(
            Arg.Any<Merchant>(),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
    }

    [Fact]
    public async Task UpdateMerchantName_WithNonExistentMerchant_ShouldReturnNotFound()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(MerchantNotFound);
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await ReceiptEndpoints.UpdateMerchantName(
            merchantId,
            new UpdateMerchantNameRequest("New Name"),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().AddAsync(
            Arg.Any<Merchant>(),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound<string>>(result);
        var notFoundResult = (Microsoft.AspNetCore.Http.HttpResults.NotFound<string>)result;
        Assert.Equal("Merchant not found.", notFoundResult.Value);
    }
}