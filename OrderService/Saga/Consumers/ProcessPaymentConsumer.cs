using MassTransit;
using OrderService.Data;
using OrderService.Saga.Contracts;

namespace OrderService.Saga.Consumers;

public class ProcessPaymentConsumer : IConsumer<ProcessPayment>
{
    private readonly ILogger<ProcessPaymentConsumer> _logger;
    private readonly OrderDbContext _dbContext;

    public ProcessPaymentConsumer(ILogger<ProcessPaymentConsumer> logger, OrderDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Processing payment for OrderId: {OrderId}, UserId: {UserId}, Amount: ₹{Amount}",
            msg.OrderId, msg.UserId, msg.Amount);

        var order = await _dbContext.Orders.FindAsync(msg.OrderId);
        if (order != null)
        {
            order.Status = "PaymentProcessing";
            await _dbContext.SaveChangesAsync();
        }

        // TODO: Replace with real payment gateway (Razorpay, Stripe, etc.)
        await Task.Delay(1000); // Simulate processing
        var paymentSucceeded = true;

        if (paymentSucceeded)
        {
            if (order != null)
            {
                order.Status = "Paid";
                await _dbContext.SaveChangesAsync();
            }

            await context.Publish(new PaymentCompleted
            {
                OrderId = msg.OrderId,
                TransactionId = $"TXN-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}"
            });

            _logger.LogInformation("Payment succeeded for OrderId: {OrderId}", msg.OrderId);
        }
        else
        {
            if (order != null)
            {
                order.Status = "PaymentFailed";
                await _dbContext.SaveChangesAsync();
            }

            await context.Publish(new PaymentFailed
            {
                OrderId = msg.OrderId,
                Reason = "Insufficient funds"
            });

            _logger.LogWarning("Payment failed for OrderId: {OrderId}", msg.OrderId);
        }
    }
}