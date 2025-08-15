using Microsoft.EntityFrameworkCore;
using UserService.Data;

namespace UserService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(8080);
            });

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            
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

            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
                                 ?? builder.Configuration.GetConnectionString("DefaultConnection");
            
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Database connection string is required");
            }

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

            var skipMigration = Environment.GetEnvironmentVariable("SKIP_DB_MIGRATION")?.ToLower() == "true";
            
            if (!skipMigration)
            {
                using (var scope = app.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
                    try
                    {
                        db.Database.Migrate();
                    }
                    catch (Exception ex)
                    {
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
