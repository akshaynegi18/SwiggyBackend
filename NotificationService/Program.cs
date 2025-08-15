using MassTransit;
using NotificationService.Events;

namespace NotificationService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("=== NotificationService Container Started ===");
            Console.WriteLine($"Current Time: {DateTime.UtcNow}");
            Console.WriteLine($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Determine which message broker to use
            var messageBrokerProvider = Environment.GetEnvironmentVariable("MessageBroker__Provider") ?? "RabbitMQ";

            Console.WriteLine($"Using message broker: {messageBrokerProvider}");

            // MassTransit setup with conditional broker
            builder.Services.AddMassTransit(x =>
            {
                x.AddConsumer<OrderPlacedEventConsumer>();
                
                if (messageBrokerProvider.Equals("AzureServiceBus", StringComparison.OrdinalIgnoreCase))
                {
                    // Azure Service Bus configuration
                    x.UsingAzureServiceBus((context, cfg) =>
                    {
                        var connectionString = Environment.GetEnvironmentVariable("AzureServiceBus__ConnectionString");
                        
                        if (string.IsNullOrEmpty(connectionString))
                        {
                            throw new InvalidOperationException("Azure Service Bus connection string is required but not provided.");
                        }
                        
                        Console.WriteLine("Configuring Azure Service Bus...");
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
                    // RabbitMQ configuration (default)
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        var rabbitMqHost = Environment.GetEnvironmentVariable("RabbitMQ__Host") ?? "rabbitmq";
                        var rabbitMqUsername = Environment.GetEnvironmentVariable("RabbitMQ__Username") ?? "guest";
                        var rabbitMqPassword = Environment.GetEnvironmentVariable("RabbitMQ__Password") ?? "guest";

                        Console.WriteLine($"RabbitMQ Config - Host: {rabbitMqHost}, Username: {rabbitMqUsername}");

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

            // Configure the HTTP request pipeline.
            app.UseSwagger();
            app.UseSwaggerUI();

            // Fix: Remove HTTPS redirection for container environments
            // app.UseHttpsRedirection(); // Comment out for Docker

            app.UseAuthorization();
            app.MapControllers();

            // Add health check
            app.MapGet("/health", () => Results.Ok("Healthy"));

            Console.WriteLine("Starting NotificationService application...");
            app.Run();
        }
    }
}
