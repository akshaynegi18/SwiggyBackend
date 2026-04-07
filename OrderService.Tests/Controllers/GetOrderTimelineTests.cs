using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OrderService.Controllers;
using OrderService.Model;
using OrderService.Services;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Controllers;

/// <summary>
/// Tests for the GET /order/timeline/{orderId} endpoint.
/// Covers: happy path, cache hit, not found, ownership security, and admin override.
/// </summary>
public class GetOrderTimelineTests : IDisposable
{
    private readonly OrderControllerTestFixture _fixture;

    public GetOrderTimelineTests()
    {
        _fixture = new OrderControllerTestFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // ── Helper ──
    private Order SeedOrderWithHistory(int orderId = 1, int userId = 1)
    {
        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            CustomerName = "Akshay",
            Item = "Paneer Tikka",
            Status = "Out for Delivery",
            CreatedAt = DateTime.UtcNow,
            DestinationLatitude = 28.6139,
            DestinationLongitude = 77.2090
        };
        _fixture.DbContext.Orders.Add(order);

        _fixture.DbContext.OrderHistories.AddRange(
            new OrderHistory { OrderId = orderId, Status = "Placed", Timestamp = DateTime.UtcNow.AddMinutes(-30) },
            new OrderHistory { OrderId = orderId, Status = "Preparing", Timestamp = DateTime.UtcNow.AddMinutes(-20) },
            new OrderHistory { OrderId = orderId, Status = "Out for Delivery", Timestamp = DateTime.UtcNow.AddMinutes(-5) }
        );
        _fixture.DbContext.SaveChanges();
        return order;
    }

    // ─────────────────────────────────────────────
    // 1. Happy path — timeline returned from database
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetTimeline_ExistingOrder_ReturnsOkWithHistory()
    {
        SeedOrderWithHistory();
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetOrderTimeline(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var history = ok.Value.Should().BeAssignableTo<List<OrderHistory>>().Subject;
        history.Should().HaveCount(3);
    }

    // ─────────────────────────────────────────────
    // 2. Timeline is returned ordered by timestamp
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetTimeline_ExistingOrder_ReturnsOrderedByTimestamp()
    {
        SeedOrderWithHistory();
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetOrderTimeline(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var history = ok.Value.Should().BeAssignableTo<List<OrderHistory>>().Subject;
        history.First().Status.Should().Be("Placed");
        history.Last().Status.Should().Be("Out for Delivery");
    }

    // ─────────────────────────────────────────────
    // 3. Cache hit — timeline returned from cache
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetTimeline_CacheHit_ReturnsTimelineFromCache()
    {
        SeedOrderWithHistory();

        var cachedTimeline = new List<OrderHistory>
        {
            new() { OrderId = 1, Status = "CachedStatus", Timestamp = DateTime.UtcNow }
        };

        _fixture.CacheServiceMock
            .Setup(c => c.GetAsync<List<OrderHistory>>(CacheKeys.GetOrderTimelineKey(1)))
            .ReturnsAsync(cachedTimeline);

        _fixture.CacheServiceMock
            .Setup(c => c.GetAsync<Order>(CacheKeys.GetOrderKey(1)))
            .ReturnsAsync(new Order { Id = 1, UserId = 1 });

        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetOrderTimeline(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var history = ok.Value.Should().BeAssignableTo<List<OrderHistory>>().Subject;
        history.Should().HaveCount(1);
        history.First().Status.Should().Be("CachedStatus");
    }

    // ─────────────────────────────────────────────
    // 4. Order not found — returns NotFound
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetTimeline_OrderNotFound_ReturnsNotFound()
    {
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetOrderTimeline(999);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 5. Different user's order — Forbid
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetTimeline_DifferentUserOrder_ReturnsForbid()
    {
        SeedOrderWithHistory(orderId: 1, userId: 5);
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetOrderTimeline(1);

        result.Should().BeOfType<ForbidResult>();
    }

    // ─────────────────────────────────────────────
    // 6. Admin can view any order timeline
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetTimeline_Admin_CanViewAnyOrderTimeline()
    {
        SeedOrderWithHistory(orderId: 1, userId: 5);
        var controller = _fixture.CreateController(userId: 99, role: "Admin");

        var result = await controller.GetOrderTimeline(1);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 7. Timeline is cached after DB fetch
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetTimeline_CacheMiss_CachesTimelineAfterFetch()
    {
        SeedOrderWithHistory();
        var controller = _fixture.CreateController(userId: 1);

        await controller.GetOrderTimeline(1);

        _fixture.CacheServiceMock.Verify(
            c => c.SetAsync(
                CacheKeys.GetOrderTimelineKey(1),
                It.IsAny<List<OrderHistory>>(),
                It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 8. Empty history — returns empty list, not 404
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetTimeline_OrderExistsNoHistory_ReturnsEmptyList()
    {
        // Seed order without any history entries
        _fixture.DbContext.Orders.Add(new Order
        {
            Id = 1,
            UserId = 1,
            CustomerName = "Akshay",
            Item = "Paneer Tikka",
            Status = "Placed",
            CreatedAt = DateTime.UtcNow
        });
        _fixture.DbContext.SaveChanges();

        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetOrderTimeline(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var history = ok.Value.Should().BeAssignableTo<List<OrderHistory>>().Subject;
        history.Should().BeEmpty();
    }
}