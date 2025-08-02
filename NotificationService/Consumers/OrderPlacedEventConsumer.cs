using MassTransit;
using NotificationService.Events;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NotificationService.Consumers
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
            var msg = context.Message;
            
            try
            {
                _logger.LogInformation("[NotificationService] Processing order notification - OrderId: {OrderId}, UserId: {UserId}, Item: {Item}", 
                    msg.OrderId, msg.UserId, msg.Item);
                
                // Here you could send an email, SMS, push notification, etc.
                // For now, we'll just log it
                _logger.LogInformation("[NotificationService] Notified user {UserId} about order {OrderId} for item {Item}", 
                    msg.UserId, msg.OrderId, msg.Item);
                    
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order notification for OrderId: {OrderId}", msg.OrderId);
                throw; // This will cause the message to be retried or moved to error queue
            }
        }
    }
}
