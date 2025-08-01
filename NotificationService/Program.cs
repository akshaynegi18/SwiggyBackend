using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Notification Service API",
        Version = "v1",
        Description = "Notification microservice deployed on Render.com"
    });
});

// Simplified MassTransit for Render.com
builder.Services.AddMassTransit(x =>
{
    // Use in-memory transport (RabbitMQ not available on free tier)
    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
    });
});

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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");
app.MapControllers();

// Root endpoint
app.MapGet("/", () => new { 
    service = "NotificationService", 
    status = "running",
    platform = "Render.com",
    timestamp = DateTime.UtcNow 
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8082";
Console.WriteLine($"NotificationService starting on port {port}");

app.Run($"http://0.0.0.0:{port}");
