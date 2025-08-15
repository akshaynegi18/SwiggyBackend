using Microsoft.EntityFrameworkCore;
using UserService.Data;

namespace UserService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("=== UserService Container Started ===");
            Console.WriteLine($"Current Time: {DateTime.UtcNow}");
            Console.WriteLine($"Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");
            
            var builder = WebApplication.CreateBuilder(args);

            // Configure Kestrel to listen on port 8080
            Console.WriteLine("Configuring Kestrel...");
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(8080);
            });

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            
            // Fix: Configure Swagger with proper version specification
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "User Service API",
                    Version = "v1",
                    Description = "A comprehensive API for managing users in the food delivery system",
                    Contact = new Microsoft.OpenApi.Models.OpenApiContact
                    {
                        Name = "Development Team",
                        Email = "dev@fooddelivery.com"
                    }
                });
            });

            // Fix: Support environment variable override
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
                                 ?? builder.Configuration.GetConnectionString("DefaultConnection");
            
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Database connection string is required");
            }

            Console.WriteLine($"Using connection string: {connectionString}");

            builder.Services.AddDbContext<UserDbContext>(options =>
                options.UseSqlServer(connectionString, sqlOptions => 
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlOptions.CommandTimeout(30);
                }));

            var app = builder.Build();

            // Fix: Add proper retry logic for database migrations
            var skipMigration = Environment.GetEnvironmentVariable("SKIP_DB_MIGRATION")?.ToLower() == "true";
            
            if (!skipMigration)
            {
                using (var scope = app.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
                    try
                    {
                        Console.WriteLine("Applying database migrations...");
                        db.Database.Migrate();
                        Console.WriteLine("Database migrations completed successfully!");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Database migration failed: {ex.Message}");
                        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                        if (environment?.ToLower() != "production")
                        {
                            throw;
                        }
                    }
                }
            }

            // Configure the HTTP request pipeline.
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "User Service API v1");
                c.DocumentTitle = "User Service API Documentation";
                c.DefaultModelsExpandDepth(-1);
                c.DisplayRequestDuration();
            });

            app.UseAuthorization();
            app.MapControllers();

            // Add health check
            app.MapGet("/health", () => Results.Ok("Healthy"));

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("UserService starting in {Environment} environment on port 8080", app.Environment.EnvironmentName);

            app.Run();
        }
    }
}
