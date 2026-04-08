using MassTransit;
using OrderService.Data;
using OrderService.Saga.Contracts;

namespace OrderService.Saga.Consumers;

public class ConfirmRestaurantConsumer : IConsumer<ConfirmRestaurant>
{
    private readonly ILogger<ConfirmRestaurantConsumer> _logger;
    private readonly OrderDbContext _dbContext;

    public ConfirmRestaurantConsumer(ILogger<ConfirmRestaurantConsumer> logger, OrderDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<ConfirmRestaurant> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Restaurant confirming OrderId: {OrderId}, Item: {Item}", msg.OrderId, msg.Item);

        var order = await _dbContext.Orders.FindAsync(msg.OrderId);
        if (order != null)
        {
            order.Status = "RestaurantConfirmation";
            await _dbContext.SaveChangesAsync();
        }

        // TODO: Replace with real restaurant API / notification
        await Task.Delay(1500); // Simulate restaurant response time
        var accepted = true;

        if (accepted)
        {
            if (order != null)
            {
                order.Status = "Preparing";
                await _dbContext.SaveChangesAsync();
            }

            await context.Publish(new RestaurantAccepted
            {
                OrderId = msg.OrderId,
                EstimatedPrepTimeMinutes = Random.Shared.Next(15, 35)
            });

            _logger.LogInformation("Restaurant accepted OrderId: {OrderId}", msg.OrderId);
        }
        else
        {
            if (order != null)
            {
                order.Status = "RestaurantRejected";
                await _dbContext.SaveChangesAsync();
            }

            await context.Publish(new RestaurantRejected
            {
                OrderId = msg.OrderId,
                Reason = "Restaurant is currently closed"
            });

            _logger.LogWarning("Restaurant rejected OrderId: {OrderId}", msg.OrderId);
        }
    }
}