using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OrderService.Controllers;
using OrderService.Model;
using OrderService.Services;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Controllers;

/// <summary>
/// Tests for the GET /order/track/{id} endpoint.
/// Covers: happy path, cache hit, 404, ownership security, and admin override.
/// </summary>
public class TrackOrderTests : IDisposable
{
    private readonly OrderControllerTestFixture _fixture;

    public TrackOrderTests()
    {
        _fixture = new OrderControllerTestFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // ── Helper ──
    private Order SeedOrder(int id = 1, int userId = 1, string status = "Placed")
    {
        var order = new Order
        {
            Id = id,
            UserId = userId,
            CustomerName = "Akshay",
            Item = "Paneer Tikka",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            DestinationLatitude = 28.6139,
            DestinationLongitude = 77.2090
        };
        _fixture.DbContext.Orders.Add(order);
        _fixture.DbContext.SaveChanges();
        return order;
    }

    // ─────────────────────────────────────────────
    // 1. Happy path — order fetched from database
    // ─────────────────────────────────────────────
    [Fact]
    public async Task TrackOrder_ExistingOrder_ReturnsOkWithOrder()
    {
        var seeded = SeedOrder();
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.TrackOrder(seeded.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var order = ok.Value.Should().BeOfType<Order>().Subject;
        order.Id.Should().Be(seeded.Id);
        order.Item.Should().Be("Paneer Tikka");
    }

    // ─────────────────────────────────────────────
    // 2. Cache hit — order returned from cache, DB not needed
    // ─────────────────────────────────────────────
    [Fact]
    public async Task TrackOrder_CacheHit_ReturnsOrderFromCache()
    {
        var cachedOrder = new Order
        {
            Id = 42,
            UserId = 1,
            CustomerName = "Cached User",
            Item = "Cached Biryani",
            Status = "Delivered"
        };

        _fixture.CacheServiceMock
            .Setup(c => c.GetAsync<Order>(CacheKeys.GetOrderKey(42)))
            .ReturnsAsync(cachedOrder);

        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.TrackOrder(42);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var order = ok.Value.Should().BeOfType<Order>().Subject;
        order.Item.Should().Be("Cached Biryani");
    }

    // ─────────────────────────────────────────────
    // 3. Order not found — returns 404
    // ─────────────────────────────────────────────
    [Fact]
    public async Task TrackOrder_OrderNotFound_ReturnsNotFound()
    {
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.TrackOrder(999);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 4. User tracks another user's order — Forbid
    // ─────────────────────────────────────────────
    [Fact]
    public async Task TrackOrder_DifferentUserOrder_ReturnsForbid()
    {
        SeedOrder(id: 1, userId: 5);
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.TrackOrder(1);

        result.Should().BeOfType<ForbidResult>();
    }

    // ─────────────────────────────────────────────
    // 5. Admin can track any order
    // ─────────────────────────────────────────────
    [Fact]
    public async Task TrackOrder_AdminTracksAnyOrder_ReturnsOk()
    {
        SeedOrder(id: 1, userId: 5);
        var controller = _fixture.CreateController(userId: 99, role: "Admin");

        var result = await controller.TrackOrder(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<Order>();
    }

    // ─────────────────────────────────────────────
    // 6. Cache is populated after a DB fetch (cache miss)
    // ─────────────────────────────────────────────
    [Fact]
    public async Task TrackOrder_CacheMiss_CachesOrderAfterDbFetch()
    {
        var seeded = SeedOrder();
        var controller = _fixture.CreateController(userId: 1);

        await controller.TrackOrder(seeded.Id);

        _fixture.CacheServiceMock.Verify(
            c => c.SetAsync(
                CacheKeys.GetOrderKey(seeded.Id),
                It.IsAny<Order>(),
                It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 7. Cache hit skips the SetAsync call
    // ─────────────────────────────────────────────
    [Fact]
    public async Task TrackOrder_CacheHit_DoesNotReCache()
    {
        _fixture.CacheServiceMock
            .Setup(c => c.GetAsync<Order>(CacheKeys.GetOrderKey(1)))
            .ReturnsAsync(new Order { Id = 1, UserId = 1, Status = "Placed" });

        var controller = _fixture.CreateController(userId: 1);

        await controller.TrackOrder(1);

        _fixture.CacheServiceMock.Verify(
            c => c.SetAsync(It.IsAny<string>(), It.IsAny<Order>(), It.IsAny<TimeSpan?>()),
            Times.Never);
    }
}