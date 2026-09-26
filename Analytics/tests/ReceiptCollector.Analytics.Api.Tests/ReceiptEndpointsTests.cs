using Microsoft.AspNetCore.Http;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Receipts;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;

namespace ReceiptCollector.Analytics.Api.Tests;

public class ReceiptEndpointsTests
{
    private static readonly Merchant? MerchantNotFound = null;
    private static readonly ReceiptDetailsDto? ReceiptNotFound = null;

    [Fact]
    public async Task GetByMerchant_UnknownMerchant_ReturnsNotFound()
    {
        // Arrange
        var service = Substitute.For<IReceiptReadService>();
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var httpContext = new DefaultHttpContext();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(MerchantNotFound);

        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await ReceiptEndpoints.GetByMerchant(
            httpContext,
            merchantId,
            service,
            merchantRepository,
            limit: 10,
            offset: 0,
            CancellationToken.None);

        // Assert
        var notFoundResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound<string>>(result);
        Assert.Equal("Merchant not found.", notFoundResult.Value);
        await merchantRepository.Received(1).GetByIdAsync(merchantId, Arg.Any<CancellationToken>());
        await service.DidNotReceive().GetByMerchantIdAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await service.DidNotReceive().GetTotalCountByMerchantIdAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByMerchant_ExistingMerchantWithoutReceipts_ReturnsEmptyOk()
    {
        // Arrange: магазин есть, чеков нет — это пустое состояние, а не «не найдено» (ADR-023, H1).
        var service = Substitute.For<IReceiptReadService>();
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var httpContext = new DefaultHttpContext();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(new Merchant(merchantId, "Магазин", MerchantCategory.Undefined));
        service.GetByMerchantIdAsync(userId, merchantId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ReceiptSummaryDto>());
        service.GetTotalCountByMerchantIdAsync(userId, merchantId, Arg.Any<CancellationToken>())
            .Returns(0);

        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await ReceiptEndpoints.GetByMerchant(
            httpContext,
            merchantId,
            service,
            merchantRepository,
            limit: 10,
            offset: 0,
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<IReadOnlyCollection<ReceiptSummaryDto>>>(result);
        Assert.Empty(okResult.Value!);
        Assert.Equal("0", httpContext.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task GetByMerchant_ExistingMerchant_ReturnsOkWithTotalCountHeader()
    {
        // Arrange: контракт для существующего магазина не изменился (200 + тот же JSON + X-Total-Count).
        var service = Substitute.For<IReceiptReadService>();
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var httpContext = new DefaultHttpContext();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var receipts = new List<ReceiptSummaryDto>
        {
            new(Guid.NewGuid(), new MerchantDto(merchantId, "Магазин", 0, null, null), 100.50m, DateTime.UtcNow),
            new(Guid.NewGuid(), new MerchantDto(merchantId, "Магазин", 0, null, null), 200.00m, DateTime.UtcNow),
        };

        merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(new Merchant(merchantId, "Магазин", MerchantCategory.Undefined));
        service.GetByMerchantIdAsync(userId, merchantId, 10, 0, Arg.Any<CancellationToken>())
            .Returns(receipts);
        service.GetTotalCountByMerchantIdAsync(userId, merchantId, Arg.Any<CancellationToken>())
            .Returns(42);

        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await ReceiptEndpoints.GetByMerchant(
            httpContext,
            merchantId,
            service,
            merchantRepository,
            limit: 10,
            offset: 0,
            CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<IReadOnlyCollection<ReceiptSummaryDto>>>(result);
        Assert.Equal(2, okResult.Value!.Count);
        Assert.Equal("42", httpContext.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task GetByMerchant_WithoutAuthenticatedUser_ReturnsBadRequest()
    {
        // Arrange: UserContext не задан — репозитории не должны вызываться вообще.
        var service = Substitute.For<IReceiptReadService>();
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var httpContext = new DefaultHttpContext();
        var merchantId = Guid.NewGuid();

        // Act
        var result = await ReceiptEndpoints.GetByMerchant(
            httpContext,
            merchantId,
            service,
            merchantRepository,
            limit: 10,
            offset: 0,
            CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>(result);
        Assert.Equal("user is not authenticated.", badRequestResult.Value);
        await merchantRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await service.DidNotReceive().GetByMerchantIdAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByMerchant_WithInvalidPaging_ReturnsBadRequest()
    {
        // Arrange
        var service = Substitute.For<IReceiptReadService>();
        var merchantRepository = Substitute.For<IMerchantRepository>();
        var httpContext = new DefaultHttpContext();
        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var context = UserContext.SetUserId(userId);

        // Act
        var negativeOffsetResult = await ReceiptEndpoints.GetByMerchant(
            httpContext, merchantId, service, merchantRepository, limit: 10, offset: -1, CancellationToken.None);
        var nonPositiveLimitResult = await ReceiptEndpoints.GetByMerchant(
            httpContext, merchantId, service, merchantRepository, limit: 0, offset: 0, CancellationToken.None);

        // Assert
        Assert.Equal("offset cannot be negative.",
            Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>(negativeOffsetResult).Value);
        Assert.Equal("limit must be greater than zero.",
            Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>(nonPositiveLimitResult).Value);
        await merchantRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_UnknownReceipt_ReturnsNotFound()
    {
        // Arrange
        var service = Substitute.For<IReceiptReadService>();
        var userId = Guid.NewGuid();

        service.GetByIdAsync(userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ReceiptNotFound);

        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await ReceiptEndpoints.GetById(Guid.NewGuid(), service, CancellationToken.None);

        // Assert
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task GetById_ExistingReceipt_ReturnsOk()
    {
        // Arrange
        var service = Substitute.For<IReceiptReadService>();
        var userId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var details = new ReceiptDetailsDto(
            receiptId,
            new MerchantDto(Guid.NewGuid(), "Магазин", 0, null, null),
            100.50m,
            DateTime.UtcNow,
            Array.Empty<ReceiptItemDto>());

        service.GetByIdAsync(userId, receiptId, Arg.Any<CancellationToken>())
            .Returns(details);

        using var context = UserContext.SetUserId(userId);

        // Act
        var result = await ReceiptEndpoints.GetById(receiptId, service, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<ReceiptDetailsDto>>(result);
        Assert.Equal(receiptId, okResult.Value!.Id);
    }

    [Fact]
    public async Task GetById_WithoutAuthenticatedUser_ReturnsBadRequest()
    {
        // Arrange
        var service = Substitute.For<IReceiptReadService>();

        // Act
        var result = await ReceiptEndpoints.GetById(Guid.NewGuid(), service, CancellationToken.None);

        // Assert
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.BadRequest<string>>(result);
        await service.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
