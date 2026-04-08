using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderService.Data;
using OrderService.Hubs;
using OrderService.Model;
using OrderService.Saga.Contracts;

namespace OrderService.Saga.Consumers;

/// <summary>
/// Listens for saga state transitions and:
///   1. Updates the Order.Status in the database
///   2. Adds an OrderHistory entry for the timeline
///   3. Sends a real-time SignalR notification to subscribed clients
/// </summary>
public class OrderFulfillmentStatusConsumer : IConsumer<OrderFulfillmentStatusChanged>
{
    private readonly OrderDbContext _context;
    private readonly IHubContext<OrderTrackingHub> _hubContext;
    private readonly ILogger<OrderFulfillmentStatusConsumer> _logger;

    public OrderFulfillmentStatusConsumer(
        OrderDbContext context,
        IHubContext<OrderTrackingHub> hubContext,
        ILogger<OrderFulfillmentStatusConsumer> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderFulfillmentStatusChanged> context)
    {
        var msg = context.Message;

        _logger.LogInformation(
            "Saga status changed — OrderId: {OrderId}, SagaState: {SagaState}, OrderStatus: {OrderStatus}",
            msg.OrderId, msg.SagaState, msg.OrderStatus);

        // 1. Update Order entity
        var order = await _context.Orders.FindAsync(msg.OrderId);
        if (order is not null)
        {
            var previousStatus = order.Status;
            order.Status = msg.OrderStatus;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Order status updated — OrderId: {OrderId}, {Previous} → {New}",
                msg.OrderId, previousStatus, msg.OrderStatus);
        }
        else
        {
            _logger.LogWarning("Order not found for status sync — OrderId: {OrderId}", msg.OrderId);
        }

        // 2. Add OrderHistory entry
        _context.OrderHistories.Add(new OrderHistory
        {
            OrderId = msg.OrderId,
            Status = msg.OrderStatus,
            Timestamp = msg.Timestamp
        });
        await _context.SaveChangesAsync();

        // 3. Send SignalR notification
        await _hubContext.Clients.Group($"order-{msg.OrderId}")
            .SendAsync("SagaStatusUpdated", new
            {
                msg.OrderId,
                msg.SagaState,
                msg.OrderStatus,
                msg.Details,
                msg.Timestamp
            });
    }
}
