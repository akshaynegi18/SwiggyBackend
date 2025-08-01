using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using MassTransit;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

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

// Database configuration with PostgreSQL URL parsing
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

if (!string.IsNullOrEmpty(databaseUrl))
{
    // Parse PostgreSQL URL format: postgresql://user:password@host:port/database
    connectionString = ParsePostgreSqlUrl(databaseUrl);
}

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(connectionString));

// Simplified MassTransit configuration
builder.Services.AddMassTransit(x =>
{
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

// Database migration with better error handling
try
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    
    Console.WriteLine($"Attempting database connection with: Host={GetHostFromConnectionString(connectionString)}");
    
    // Test connection first
    if (await context.Database.CanConnectAsync())
    {
        Console.WriteLine("Database connection successful, applying migrations...");
        await context.Database.MigrateAsync();
        Console.WriteLine("OrderService database migration completed successfully.");
    }
    else
    {
        Console.WriteLine("Cannot connect to database. Service will start without database.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"OrderService database error: {ex.Message}");
    Console.WriteLine("Service will continue without database migration.");
}

// Configure pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Service API V1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// Root endpoint
app.MapGet("/", () => new { 
    service = "OrderService", 
    status = "running",
    platform = "Render.com",
    swagger = "/swagger",
    database = connectionString != null ? "configured" : "not configured",
    timestamp = DateTime.UtcNow 
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Console.WriteLine($"OrderService starting on port {port}");

app.Run($"http://0.0.0.0:{port}");

// Helper method to parse PostgreSQL URL
static string ParsePostgreSqlUrl(string databaseUrl)
{
    try
    {
        var uri = new Uri(databaseUrl);
        var host = uri.Host;
        var port = uri.Port;
        var database = uri.AbsolutePath.Trim('/');
        var username = uri.UserInfo.Split(':')[0];
        var password = uri.UserInfo.Split(':')[1];

        var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
        Console.WriteLine($"Parsed connection string: Host={host};Port={port};Database={database};Username={username}");
        return connectionString;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to parse database URL: {ex.Message}");
        return "Host=localhost;Database=OrderDb;Username=postgres;Password=password";
    }
}

static string GetHostFromConnectionString(string connectionString)
{
    try
    {
        var match = Regex.Match(connectionString, @"Host=([^;]+)");
        return match.Success ? match.Groups[1].Value : "unknown";
    }
    catch
    {
        return "unknown";
    }
}
