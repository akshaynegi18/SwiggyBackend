using MassTransit;

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

            // Fix: Support environment variable configuration for RabbitMQ
            var rabbitMqHost = Environment.GetEnvironmentVariable("RabbitMQ__Host") ?? "rabbitmq";
            var rabbitMqUsername = Environment.GetEnvironmentVariable("RabbitMQ__Username") ?? "guest";
            var rabbitMqPassword = Environment.GetEnvironmentVariable("RabbitMQ__Password") ?? "guest";

            Console.WriteLine($"RabbitMQ Config - Host: {rabbitMqHost}, Username: {rabbitMqUsername}");

            // MassTransit + RabbitMQ setup
            builder.Services.AddMassTransit(x =>
            {
                x.AddConsumer<OrderPlacedEventConsumer>();
                x.UsingRabbitMq((context, cfg) =>
                {
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
