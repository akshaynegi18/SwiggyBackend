using MassTransit;
using NotificationService.Events;

namespace NotificationService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var messageBrokerProvider = Environment.GetEnvironmentVariable("MessageBroker__Provider") ?? "RabbitMQ";

            builder.Services.AddMassTransit(x =>
            {
                x.AddConsumer<OrderPlacedEventConsumer>();
                
                if (messageBrokerProvider.Equals("AzureServiceBus", StringComparison.OrdinalIgnoreCase))
                {
                    x.UsingAzureServiceBus((context, cfg) =>
                    {
                        var connectionString = Environment.GetEnvironmentVariable("AzureServiceBus__ConnectionString");
                        
                        if (string.IsNullOrEmpty(connectionString))
                        {
                            throw new InvalidOperationException("Azure Service Bus connection string is required but not provided.");
                        }
                        
                        cfg.Host(connectionString);
                        
                        // Configure topic for the event
                        // Update the SubscriptionEndpoint configuration to use the correct overload
                        cfg.SubscriptionEndpoint("notification-service-subscription", "order-events", e =>
                        {
                            e.ConfigureConsumer<OrderPlacedEventConsumer>(context);
                        });
                        cfg.Message<OrderPlacedEvent>(x => x.SetEntityName("order-events"));
                        
                        
                    });
                }
                else
                {
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        var rabbitMqHost = Environment.GetEnvironmentVariable("RabbitMQ__Host") ?? "rabbitmq";
                        var rabbitMqUsername = Environment.GetEnvironmentVariable("RabbitMQ__Username") ?? "guest";
                        var rabbitMqPassword = Environment.GetEnvironmentVariable("RabbitMQ__Password") ?? "guest";

                        cfg.Host(rabbitMqHost, "/", h =>
                        {
                            h.Username(rabbitMqUsername);
                            h.Password(rabbitMqPassword);
                        });
                        
                        cfg.ReceiveEndpoint("order-placed-notificationservice", e =>
                        {
                            e.ConfigureConsumer<OrderPlacedEventConsumer>(context);
                        });
                    });
                }
            });

            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseAuthorization();
            app.MapControllers();

            app.MapGet("/health", () => Results.Ok("Healthy"));

            app.Run();
        }
    }
}
