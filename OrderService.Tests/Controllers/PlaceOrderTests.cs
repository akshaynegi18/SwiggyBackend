using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OrderService.Controllers;
using OrderService.Events;
using OrderService.Model;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Controllers;

/// <summary>
/// Tests for the POST /order/place endpoint.
/// Covers: happy path, security, caching, and event publishing.
/// </summary>
public class PlaceOrderTests : IDisposable
{
    private readonly OrderControllerTestFixture _fixture;

    public PlaceOrderTests()
    {
        _fixture = new OrderControllerTestFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // ── Helper ──
    private static PlaceOrderDto CreateValidDto(int userId = 1) => new()
    {
        UserId = userId,
        CustomerName = "Akshay",
        Item = "Paneer Tikka",
        DestinationLatitude = 28.6139,
        DestinationLongitude = 77.2090
    };

    // ─────────────────────────────────────────────
    // 1. Happy path — order saved, cached, event published
    // ─────────────────────────────────────────────
    [Fact]
    public async Task PlaceOrder_ValidOrder_ReturnsOkWithOrder()
    {
        // Arrange
        var controller = _fixture.CreateController(userId: 1);
        var dto = CreateValidDto();

        // Act
        var result = await controller.PlaceOrder(dto);

        // Assert — returns 200 with Order object
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var order = okResult.Value.Should().BeOfType<Order>().Subject;

        order.CustomerName.Should().Be("Akshay");
        order.Item.Should().Be("Paneer Tikka");
        order.UserId.Should().Be(1);
        order.Status.Should().Be("Placed");
    }

    // ─────────────────────────────────────────────
    // 2. Order is persisted to database
    // ─────────────────────────────────────────────
    [Fact]
    public async Task PlaceOrder_ValidOrder_SavesOrderToDatabase()
    {
        var controller = _fixture.CreateController(userId: 1);
        var dto = CreateValidDto();

        await controller.PlaceOrder(dto);

        _fixture.DbContext.Orders.Should().HaveCount(1);
        var saved = _fixture.DbContext.Orders.First();
        saved.Item.Should().Be("Paneer Tikka");
    }

    // ─────────────────────────────────────────────
    // 3. MassTransit event is published after placing order
    // ─────────────────────────────────────────────
    [Fact]
    public async Task PlaceOrder_ValidOrder_PublishesOrderPlacedEvent()
    {
        var controller = _fixture.CreateController(userId: 1);
        var dto = CreateValidDto();

        await controller.PlaceOrder(dto);

        _fixture.PublishEndpointMock.Verify(
            p => p.Publish(
                It.Is<OrderPlacedEvent>(e =>
                    e.Item == "Paneer Tikka" && e.UserId == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 4. Cache is set after placing order
    // ─────────────────────────────────────────────
    [Fact]
    public async Task PlaceOrder_ValidOrder_CachesTheOrder()
    {
        var controller = _fixture.CreateController(userId: 1);
        var dto = CreateValidDto();

        await controller.PlaceOrder(dto);

        _fixture.CacheServiceMock.Verify(
            c => c.SetAsync(
                It.Is<string>(key => key.StartsWith("order:")),
                It.IsAny<Order>(),
                It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 5. User tries to place order for a different userId — forbidden
    // ─────────────────────────────────────────────
    [Fact]
    public async Task PlaceOrder_UserIdMismatch_ReturnsForbid()
    {
        // Authenticated as userId=1, but DTO says userId=999
        var controller = _fixture.CreateController(userId: 1);
        var dto = CreateValidDto(userId: 999);

        var result = await controller.PlaceOrder(dto);

        result.Should().BeOfType<ForbidResult>();
    }

    // ─────────────────────────────────────────────
    // 6. No order saved when userId mismatches (Forbid)
    // ─────────────────────────────────────────────
    [Fact]
    public async Task PlaceOrder_UserIdMismatch_DoesNotSaveOrder()
    {
        var controller = _fixture.CreateController(userId: 1);
        var dto = CreateValidDto(userId: 999);

        await controller.PlaceOrder(dto);

        _fixture.DbContext.Orders.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────
    // 7. Order uses authenticated userId, not DTO manipulation
    // ─────────────────────────────────────────────
    [Fact]
    public async Task PlaceOrder_ValidOrder_UsesAuthenticatedUserId()
    {
        var controller = _fixture.CreateController(userId: 42);
        var dto = CreateValidDto(userId: 42);

        var result = await controller.PlaceOrder(dto);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var order = okResult.Value.Should().BeOfType<Order>().Subject;
        order.UserId.Should().Be(42);
    }
}