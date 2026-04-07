using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OrderService.Controllers;
using OrderService.Model;
using OrderService.Services;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Controllers;

/// <summary>
/// Tests for the POST /order/update-status endpoint.
/// Covers: happy path, not found, history logging, cache updates, and SignalR broadcast.
/// </summary>
public class UpdateOrderStatusTests : IDisposable
{
    private readonly OrderControllerTestFixture _fixture;

    public UpdateOrderStatusTests()
    {
        _fixture = new OrderControllerTestFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // ── Helpers ──
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

    private static OrderStatusUpdateDto CreateUpdateDto(int orderId = 1, string status = "Preparing") => new()
    {
        OrderId = orderId,
        Status = status
    };

    // ─────────────────────────────────────────────
    // 1. Happy path — status updated, returns Ok
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_ValidUpdate_ReturnsOkWithUpdatedOrder()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "Admin");
        var dto = CreateUpdateDto(orderId: 1, status: "Preparing");

        var result = await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var order = ok.Value.Should().BeOfType<Order>().Subject;
        order.Status.Should().Be("Preparing");
    }

    // ─────────────────────────────────────────────
    // 2. Order not found — returns 404
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_OrderNotFound_ReturnsNotFound()
    {
        var controller = _fixture.CreateController(userId: 1, role: "Admin");
        var dto = CreateUpdateDto(orderId: 999);

        var result = await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 3. OrderHistory entry is created
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_ValidUpdate_CreatesOrderHistoryEntry()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "Admin");
        var dto = CreateUpdateDto(orderId: 1, status: "Out for Delivery");

        await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        _fixture.DbContext.OrderHistories.Should().HaveCount(1);
        var history = _fixture.DbContext.OrderHistories.First();
        history.OrderId.Should().Be(1);
        history.Status.Should().Be("Out for Delivery");
    }

    // ─────────────────────────────────────────────
    // 4. Status is persisted to database
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_ValidUpdate_PersistsNewStatusToDb()
    {
        SeedOrder(id: 1, status: "Placed");
        var controller = _fixture.CreateController(userId: 1, role: "Admin");
        var dto = CreateUpdateDto(orderId: 1, status: "Delivered");

        await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        var saved = _fixture.DbContext.Orders.First(o => o.Id == 1);
        saved.Status.Should().Be("Delivered");
    }

    // ─────────────────────────────────────────────
    // 5. Cache is updated after status change
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_ValidUpdate_UpdatesOrderCache()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "Admin");
        var dto = CreateUpdateDto(orderId: 1, status: "Delivered");

        await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        _fixture.CacheServiceMock.Verify(
            c => c.SetAsync(
                CacheKeys.GetOrderKey(1),
                It.Is<Order>(o => o.Status == "Delivered"),
                It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 6. Timeline cache is invalidated
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_ValidUpdate_InvalidatesTimelineCache()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "Admin");
        var dto = CreateUpdateDto(orderId: 1, status: "Preparing");

        await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        _fixture.CacheServiceMock.Verify(
            c => c.RemoveAsync(CacheKeys.GetOrderTimelineKey(1)),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 7. SignalR broadcast is sent to the order group
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_ValidUpdate_BroadcastsViaSignalR()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 1, role: "Admin");
        var dto = CreateUpdateDto(orderId: 1, status: "Preparing");

        await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        var mockClients = Mock.Get(_fixture.HubContextMock.Object.Clients);
        mockClients.Verify(c => c.Group("order-1"), Times.Once);
    }

    // ─────────────────────────────────────────────
    // 8. DeliveryPartner role can also update status
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_DeliveryPartnerRole_ReturnsOk()
    {
        SeedOrder();
        var controller = _fixture.CreateController(userId: 2, role: "DeliveryPartner");
        var dto = CreateUpdateDto(orderId: 1, status: "Out for Delivery");

        var result = await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 9. No history entry when order is not found
    // ─────────────────────────────────────────────
    [Fact]
    public async Task UpdateOrderStatus_OrderNotFound_DoesNotCreateHistory()
    {
        var controller = _fixture.CreateController(userId: 1, role: "Admin");
        var dto = CreateUpdateDto(orderId: 999);

        await controller.UpdateOrderStatus(dto, _fixture.HubContextMock.Object);

        _fixture.DbContext.OrderHistories.Should().BeEmpty();
    }
}