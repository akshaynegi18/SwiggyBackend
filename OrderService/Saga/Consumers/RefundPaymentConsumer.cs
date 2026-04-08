using MassTransit;
using OrderService.Data;
using OrderService.Saga.Contracts;

namespace OrderService.Saga.Consumers;

/// <summary>
/// Compensating transaction — refunds payment when a downstream step fails.
/// </summary>
public class RefundPaymentConsumer : IConsumer<RefundPayment>
{
    private readonly ILogger<RefundPaymentConsumer> _logger;
    private readonly OrderDbContext _dbContext;

    public RefundPaymentConsumer(ILogger<RefundPaymentConsumer> logger, OrderDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<RefundPayment> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Processing refund for OrderId: {OrderId}, UserId: {UserId}, Reason: {Reason}",
            msg.OrderId, msg.UserId, msg.Reason);

        var order = await _dbContext.Orders.FindAsync(msg.OrderId);
        if (order != null)
        {
            order.Status = "Refunding";
            await _dbContext.SaveChangesAsync();
        }

        // TODO: Replace with real payment gateway refund API
        await Task.Delay(500);

        if (order != null)
        {
            order.Status = "Refunded";
            await _dbContext.SaveChangesAsync();
        }

        await context.Publish(new RefundCompleted { OrderId = msg.OrderId });

        _logger.LogInformation("Refund completed for OrderId: {OrderId}", msg.OrderId);
    }
}