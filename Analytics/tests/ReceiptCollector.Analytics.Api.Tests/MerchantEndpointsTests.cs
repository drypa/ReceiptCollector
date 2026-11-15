using Microsoft.AspNetCore.Http;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Merchants;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Models;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
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
        var result = await MerchantEndpoints.UpdateMerchantName(
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
        var result = await MerchantEndpoints.UpdateMerchantName(
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
        var result = await MerchantEndpoints.UpdateMerchantName(
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

    [Fact]
    public async Task UpdateMerchantName_WithEmptyName_ReturnsBadRequest()
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
        var result = await MerchantEndpoints.UpdateMerchantName(
            merchantId,
            new UpdateMerchantNameRequest(""),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().AddAsync(
            Arg.Any<Merchant>(),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>(result);
        var badRequestResult = (Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>)result;
        Assert.Equal("Merchant name is required.", badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateMerchantName_WithWhitespaceName_ReturnsBadRequest()
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
        var result = await MerchantEndpoints.UpdateMerchantName(
            merchantId,
            new UpdateMerchantNameRequest("   "),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().AddAsync(
            Arg.Any<Merchant>(),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>(result);
        var badRequestResult = (Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>)result;
        Assert.Equal("Merchant name is required.", badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateMerchantName_WithTooLongName_ReturnsBadRequest()
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
        var result = await MerchantEndpoints.UpdateMerchantName(
            merchantId,
            new UpdateMerchantNameRequest(new string('а', 257)),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().AddAsync(
            Arg.Any<Merchant>(),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>(result);
        var badRequestResult = (Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>)result;
        Assert.Equal("Merchant name must be at most 256 characters.", badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateMerchantName_WithMaxLengthName_Succeeds()
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

        var maxLengthName = new string('а', 256);

        // Act
        var result = await MerchantEndpoints.UpdateMerchantName(
            merchantId,
            new UpdateMerchantNameRequest(maxLengthName),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.Received(1).AddAsync(
            Arg.Is<Merchant>(m => m.Name == maxLengthName),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>(result);
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<string>)result;
        Assert.Equal("Merchant name updated successfully.", okResult.Value);
    }

    [Fact]
    public async Task UpdateMerchantName_TrimsWhitespace()
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
        var result = await MerchantEndpoints.UpdateMerchantName(
            merchantId,
            new UpdateMerchantNameRequest("  Пятёрочка  "),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.Received(1).AddAsync(
            Arg.Is<Merchant>(m => m.Name == "Пятёрочка"),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>(result);
        var okResult = (Microsoft.AspNetCore.Http.HttpResults.Ok<string>)result;
        Assert.Equal("Merchant name updated successfully.", okResult.Value);
    }

    [Fact]
    public async Task GetAllMerchants_WithAdminUser_ReturnsMerchantList()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var merchants = new List<Merchant>
        {
            new(Guid.NewGuid(), "Магнит", MerchantCategory.Undefined, null, null),
            new(Guid.NewGuid(), "Пятёрочка", MerchantCategory.GroceryStores, "ул. Ленина, 1", "7712345678"),
        };
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(50, 0, null, Arg.Any<CancellationToken>())
            .Returns((merchants, 2));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: null,
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        Assert.Equal(2, okResult.Value!.Count);
        Assert.Equal("Магнит", okResult.Value[0].Name);
        Assert.Equal((int)MerchantCategory.GroceryStores, okResult.Value[1].Category);
        Assert.Equal("2", httpContext.Response.Headers["X-Total-Count"]);
        await merchantRepository.Received(1).GetPageAsync(50, 0, null, Arg.Any<CancellationToken>());
        await merchantRepository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllMerchants_WithSearch_ReturnsFilteredList()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var merchants = new List<Merchant>
        {
            new(Guid.NewGuid(), "Магнит", MerchantCategory.GroceryStores),
        };
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(1, 0, "маг", Arg.Any<CancellationToken>())
            .Returns((merchants, 1));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 1,
            offset: 0,
            search: "маг",
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        var merchant = Assert.Single(okResult.Value!);
        Assert.Equal("Магнит", merchant.Name);
        Assert.Equal("1", httpContext.Response.Headers["X-Total-Count"]);
        await merchantRepository.Received(1).GetPageAsync(1, 0, "маг", Arg.Any<CancellationToken>());
        await merchantRepository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllMerchants_WithNonAdminUser_ReturnsForbidden()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var user = new User(userId, "userName", "12345", 111222, isAdmin: false);

        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: null,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
        await merchantRepository.DidNotReceive().GetPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
    }

    [Fact]
    public async Task GetAllMerchants_WithoutUser_ReturnsUnauthorized()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var httpContext = new DefaultHttpContext();

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: null,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().GetPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task GetAllMerchants_WithSearchMatchingName_PassesTrimmedTerm_PartialCaseInsensitive()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var merchants = new List<Merchant> { new(Guid.NewGuid(), "Пятёрочка", MerchantCategory.GroceryStores) };
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(50, 0, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((merchants, 1));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: "  ПяТ  ",
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        Assert.Equal("Пятёрочка", Assert.Single(okResult.Value!).Name);
        Assert.Equal("1", httpContext.Response.Headers["X-Total-Count"]);
        await merchantRepository.Received(1).GetPageAsync(50, 0, "ПяТ", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllMerchants_WithSearchMatchingInn_ReturnsResult()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var merchants = new List<Merchant> { new(Guid.NewGuid(), "Магнит", MerchantCategory.GroceryStores, null, "7702000002") };
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(50, 0, "7702", Arg.Any<CancellationToken>())
            .Returns((merchants, 1));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: "7702",
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        var merchant = Assert.Single(okResult.Value!);
        Assert.Equal("Магнит", merchant.Name);
        Assert.Equal("7702000002", merchant.Inn);
        Assert.Equal("1", httpContext.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task GetAllMerchants_WithSearchMatchingNameOfOneAndInnOfAnother_ReturnsBoth()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var merchants = new List<Merchant>
        {
            new(Guid.NewGuid(), "Shop 123", MerchantCategory.Undefined, null, "9900000001"),
            new(Guid.NewGuid(), "Другой", MerchantCategory.Undefined, null, "7700123999"),
        };
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(50, 0, "123", Arg.Any<CancellationToken>())
            .Returns((merchants, 2));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: "123",
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        Assert.Equal(2, okResult.Value!.Count);
        Assert.Contains(okResult.Value, m => m.Name == "Shop 123");
        Assert.Contains(okResult.Value, m => m.Name == "Другой");
        Assert.Equal("2", httpContext.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task GetAllMerchants_WithNullInnMerchant_FoundByName()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var merchants = new List<Merchant> { new(Guid.NewGuid(), "БезИНН Магазин", MerchantCategory.Undefined, null, null) };
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(50, 0, "безинн", Arg.Any<CancellationToken>())
            .Returns((merchants, 1));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: "безинн",
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        var merchant = Assert.Single(okResult.Value!);
        Assert.Equal("БезИНН Магазин", merchant.Name);
        Assert.Null(merchant.Inn);
    }

    [Fact]
    public async Task GetAllMerchants_WithWhitespaceSearch_TreatsAsNoFilter()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var merchants = new List<Merchant> { new(Guid.NewGuid(), "Пятёрочка", MerchantCategory.GroceryStores) };
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(50, 0, null, Arg.Any<CancellationToken>())
            .Returns((merchants, 1));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: "   ",
            CancellationToken.None);

        // Assert
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        await merchantRepository.Received(1).GetPageAsync(50, 0, null, Arg.Any<CancellationToken>());
        Assert.Equal("1", httpContext.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task GetAllMerchants_WithEmptyResult_ReturnsOkEmptyArrayAndZeroTotal()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(50, 0, "нет", Arg.Any<CancellationToken>())
            .Returns((new List<Merchant>(), 0));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 50,
            offset: 0,
            search: "нет",
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        Assert.Empty(okResult.Value!);
        Assert.Equal("0", httpContext.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task GetAllMerchants_WithLimitAndOffset_ReturnsPageAndTotalFromFilteredSet()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var page = new List<Merchant> { new(Guid.NewGuid(), "Бета", MerchantCategory.Undefined, null, "1000000002") };
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetPageAsync(1, 1, "1000", Arg.Any<CancellationToken>())
            .Returns((page, 3));
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        var httpContext = new DefaultHttpContext();
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.GetAll(
            httpContext,
            merchantRepository,
            userRepository,
            limit: 1,
            offset: 1,
            search: "1000",
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantDto>>>(result);
        Assert.Equal("Бета", Assert.Single(okResult.Value!).Name);
        Assert.Equal("3", httpContext.Response.Headers["X-Total-Count"]);
        await merchantRepository.Received(1).GetPageAsync(1, 1, "1000", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMerchantCategory_WithAdminUser_UpdatesSuccessfully()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var existingMerchant = new Merchant(merchantId, "Пятёрочка", MerchantCategory.Undefined);
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(existingMerchant);
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.UpdateCategory(
            merchantId,
            new UpdateMerchantCategoryRequest((int)MerchantCategory.GroceryStores),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.Received(1).UpdateCategoryAsync(
            merchantId,
            MerchantCategory.GroceryStores,
            Arg.Any<CancellationToken>());
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task UpdateMerchantCategory_WithInvalidCategory_ReturnsBadRequest()
    {
        // Arrange
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var userRepository = Substitute.For<IUserRepository>();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var existingMerchant = new Merchant(merchantId, "Пятёрочка", MerchantCategory.Undefined);
        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(existingMerchant);
        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.UpdateCategory(
            merchantId,
            new UpdateMerchantCategoryRequest(999),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().UpdateCategoryAsync(
            Arg.Any<Guid>(),
            Arg.Any<MerchantCategory>(),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>(result);
        var badRequestResult = (Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>)result;
        Assert.Equal("Invalid category.", badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateMerchantCategory_WithNonExistentMerchant_ReturnsNotFound()
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
        var result = await MerchantEndpoints.UpdateCategory(
            merchantId,
            new UpdateMerchantCategoryRequest((int)MerchantCategory.GroceryStores),
            merchantRepository,
            userRepository,
            CancellationToken.None);

        // Assert
        await merchantRepository.DidNotReceive().UpdateCategoryAsync(
            Arg.Any<Guid>(),
            Arg.Any<MerchantCategory>(),
            Arg.Any<CancellationToken>());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound<string>>(result);
        var notFoundResult = (Microsoft.AspNetCore.Http.HttpResults.NotFound<string>)result;
        Assert.Equal("Merchant not found.", notFoundResult.Value);
    }

    [Fact]
    public async Task GetMerchantCategories_ReturnsAllCategories()
    {
        // Arrange
        var userRepository = Substitute.For<IUserRepository>();
        var userId = Guid.NewGuid();

        var user = new User(userId, "userName", "12345", 111222, isAdmin: true);

        userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);

        // Устанавливаем UserId в UserContext
        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await MerchantEndpoints.ListCategories(userRepository, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<List<MerchantCategoryDto>>>(result);
        var categories = okResult.Value!;
        Assert.Equal(Enum.GetValues<MerchantCategory>().Length, categories.Count);
        Assert.All(Enum.GetValues<MerchantCategory>(), category =>
        {
            var dto = Assert.Single(categories, c => c.Id == (int)category);
            Assert.Equal(MerchantCategoryHelper.GetDisplayName(category), dto.Name);
        });
        Assert.Equal(MerchantCategoryHelper.GetDisplayName(MerchantCategory.Undefined),
            categories.Single(c => c.Id == (int)MerchantCategory.Undefined).Name);
    }
}
