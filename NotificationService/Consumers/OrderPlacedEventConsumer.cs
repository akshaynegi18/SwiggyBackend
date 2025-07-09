using MassTransit;
using NotificationService.Events;
using System.Threading.Tasks;

namespace NotificationService.Consumers
{
    public class OrderPlacedEventConsumer : IConsumer<OrderPlacedEvent>
    {
        public Task Consume(ConsumeContext<OrderPlacedEvent> context)
        {
            var msg = context.Message;
            Console.WriteLine($"[NotificationService] Notifying user {msg.UserId} about order {msg.OrderId} for item {msg.Item}.");
            // Here you could send an email, SMS, push notification, etc.
            return Task.CompletedTask;
        }
    }
}