using MassTransit;
using NotificationService.Consumers;


namespace NotificationService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { Title = "Notification Service API", Version = "v1" });
            });

            // Configure RabbitMQ for Azure
            builder.Services.AddMassTransit(x =>
            {
                x.AddConsumer<OrderPlacedEventConsumer>();
                x.UsingRabbitMq((context, cfg) =>
                {
                    var rabbitMqHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
                    var rabbitMqUsername = builder.Configuration["RabbitMQ:Username"] ?? "guest";
                    var rabbitMqPassword = builder.Configuration["RabbitMQ:Password"] ?? "guest";
                    
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

            // Add CORS for Azure deployment
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            else
            {
                // Enable Swagger in production for Azure App Service
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Notification Service API v1");
                    c.RoutePrefix = "swagger";
                });
            }

            app.UseCors("AllowAll");
            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();

            // Add health check endpoints
            app.MapGet("/", () => Results.Ok(new { status = "healthy", service = "NotificationService" }));
            app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "NotificationService", timestamp = DateTime.UtcNow }));

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("NotificationService starting in {Environment} environment", app.Environment.EnvironmentName);

            app.Run();
        }
    }
}
