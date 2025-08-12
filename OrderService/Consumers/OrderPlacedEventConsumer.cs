using MassTransit;
using OrderService.Events;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OrderService.Consumers
{
    public class OrderPlacedEventConsumer : IConsumer<OrderPlacedEvent>
    {
        private readonly ILogger<OrderPlacedEventConsumer> _logger;

        public OrderPlacedEventConsumer(ILogger<OrderPlacedEventConsumer> logger)
        {
            _logger = logger;
        }

        public Task Consume(ConsumeContext<OrderPlacedEvent> context)
        {
            try
            {
                _logger.LogInformation("Order placed event received: OrderId: {OrderId}, UserId: {UserId}, Item: {Item}", 
                    context.Message.OrderId, context.Message.UserId, context.Message.Item);
                
                Console.WriteLine($"Order received: {context.Message.OrderId}, User: {context.Message.UserId}");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OrderPlacedEvent for OrderId: {OrderId}", context.Message.OrderId);
                throw;
            }
        }
    }
}