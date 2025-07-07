using MassTransit;
using OrderService.Events;
using System.Threading.Tasks;

public class OrderPlacedEventConsumer : IConsumer<OrderPlacedEvent>
{
    public Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        Console.WriteLine($"Order received: {context.Message.OrderId}, User: {context.Message.UserId}");
        return Task.CompletedTask;
    }
}