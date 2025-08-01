using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using MassTransit;
using OrderService.Hubs;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Order Service API",
        Version = "v1",
        Description = "Order management microservice deployed on Render.com"
    });
});

// Database configuration for Render.com PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

if (!string.IsNullOrEmpty(databaseUrl))
{
    connectionString = databaseUrl;
}

// Switch to PostgreSQL
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(connectionString));

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Simplified MassTransit configuration for Render.com
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedEventConsumer>();
    
    // Use in-memory transport for now (RabbitMQ not available on free tier)
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddSignalR();

var app = builder.Build();

// Database migration
try
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    context.Database.Migrate();
    Console.WriteLine("OrderService database migration completed successfully.");
}
catch (Exception ex)
{
    Console.WriteLine($"OrderService database migration failed: {ex.Message}");
}

// Enable Swagger for all environments
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Service API V1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();
app.MapHub<OrderTrackingHub>("/order-tracking-hub");

// Root endpoint
app.MapGet("/", () => new { 
    service = "OrderService", 
    status = "running",
    platform = "Render.com",
    swagger = "/swagger",
    timestamp = DateTime.UtcNow 
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Console.WriteLine($"OrderService starting on port {port}");

app.Run($"http://0.0.0.0:{port}");
