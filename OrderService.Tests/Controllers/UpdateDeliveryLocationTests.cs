using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OrderService.Controllers;
using OrderService.Model;
using OrderService.Services;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Controllers;

/// <summary>
/// Tests for the POST /order/update-location endpoint.
/// Covers: happy path, ETA calculation, not found, history, cache, and SignalR.
/// </summary>
public class UpdateDeliveryLocationTests : IDisposable
{
    private readonly OrderControllerTestFixture _fixture;

    public UpdateDeliveryLocationTests()
    {
        _fixture = new OrderControllerTestFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // ── Helper ──
    private Order SeedOrder(int id = 1, int userId = 1)
    {
        var order = new Order
        {
            Id = id,
            UserId = userId,
            CustomerName = "Akshay",
            Item = "Paneer Tikka",
            Status = "Out for Delivery",
            CreatedAt = DateTime.UtcNow,
            DestinationLatitude = 28.6139,
            DestinationLongitude = 77.2090
        };
        _fixture.DbContext.Orders.Add(order);
        _fixture.DbContext.SaveChanges();
        return order;
    }

    private static DeliveryLocationUpdateDto CreateLocationDto(int orderId = 1) => new()
    {
        OrderId = orderId,
        Latitude = 28.6200,
        Longitude = 77.2100
    };

    // ─────────────────────────────────────────────
    // 1. Happy path — location updated, returns Ok with ETA
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateLocation_ValidUpdate_ReturnsOkWithEta()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "DeliveryPartner");
        var dto = CreateLocationDto();

        var result = await controller.UpdateDeliveryLocation(dto, _fixture.HubContextMock.Object);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var order = ok.Value.Should().BeOfType<Order>().Subject;
        order.DeliveryLatitude.Should().Be(28.6200);
        order.DeliveryLongitude.Should().Be(77.2100);
        order.ETA.Should().NotBeNull().And.BeGreaterThan(0);
    }

    // ─────────────────────────────────────────────
    // 2. Order not found — returns 404
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateLocation_OrderNotFound_ReturnsNotFound()
    {
        var controller = _fixture.CreateController(userId: 1, role: "DeliveryPartner");
        var dto = CreateLocationDto(orderId: 999);

        var result = await controller.UpdateDeliveryLocation(dto, _fixture.HubContextMock.Object);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 3. OrderHistory entry is created with coordinates
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateLocation_ValidUpdate_CreatesOrderHistoryEntry()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "DeliveryPartner");
        var dto = CreateLocationDto();

        await controller.UpdateDeliveryLocation(dto, _fixture.HubContextMock.Object);

        _fixture.DbContext.OrderHistories.Should().HaveCount(1);
        var history = _fixture.DbContext.OrderHistories.First();
        history.OrderId.Should().Be(1);
        history.DeliveryLatitude.Should().Be(28.6200);
        history.DeliveryLongitude.Should().Be(77.2100);
    }

    // ─────────────────────────────────────────────
    // 4. Location coordinates are persisted to database
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateLocation_ValidUpdate_PersistsCoordinatesToDb()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "DeliveryPartner");
        var dto = CreateLocationDto();

        await controller.UpdateDeliveryLocation(dto, _fixture.HubContextMock.Object);

        var saved = _fixture.DbContext.Orders.First(o => o.Id == 1);
        saved.DeliveryLatitude.Should().Be(28.6200);
        saved.DeliveryLongitude.Should().Be(77.2100);
        saved.ETA.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────
    // 5. Cache is updated after location change
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateLocation_ValidUpdate_UpdatesOrderCache()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "DeliveryPartner");
        var dto = CreateLocationDto();

        await controller.UpdateDeliveryLocation(dto, _fixture.HubContextMock.Object);

        _fixture.CacheServiceMock.Verify(
            c => c.SetAsync(
                CacheKeys.GetOrderKey(1),
                It.Is<Order>(o => o.DeliveryLatitude == 28.6200),
                It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 6. Timeline cache is invalidated
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateLocation_ValidUpdate_InvalidatesTimelineCache()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "DeliveryPartner");
        var dto = CreateLocationDto();

        await controller.UpdateDeliveryLocation(dto, _fixture.HubContextMock.Object);

        _fixture.CacheServiceMock.Verify(
            c => c.RemoveAsync(CacheKeys.GetOrderTimelineKey(1)),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 7. SignalR location broadcast is sent
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateLocation_ValidUpdate_BroadcastsViaSignalR()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "DeliveryPartner");
        var dto = CreateLocationDto();

        await controller.UpdateDeliveryLocation(dto, _fixture.HubContextMock.Object);

        var mockClients = Mock.Get(_fixture.HubContextMock.Object.Clients);
        mockClients.Verify(c => c.Group("order-1"), Times.Once);
    }

    // ─────────────────────────────────────────────
    // 8. No history entry when order is not found
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateLocation_OrderNotFound_DoesNotCreateHistory()
    {
        var controller = _fixture.CreateController(userId: 1, role: "DeliveryPartner");
        var dto = CreateLocationDto(orderId: 999);

        await controller.UpdateDeliveryLocation(dto, _fixture.HubContextMock.Object);

        _fixture.DbContext.OrderHistories.Should().BeEmpty();
    }
}