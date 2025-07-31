using Microsoft.EntityFrameworkCore;
using UserService.Data;

namespace UserService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "User Service API",
                    Version = "v1",
                    Description = "User management microservice deployed on Render.com"
                });
            });

            // Database configuration for Render.com PostgreSQL
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            
            if (!string.IsNullOrEmpty(databaseUrl))
            {
                connectionString = databaseUrl;
            }

            builder.Services.AddDbContext<UserDbContext>(options =>
                options.UseNpgsql(connectionString));

            // Add CORS for API access
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

            // Database migration
            try
            {
                using var scope = app.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<UserDbContext>();
                context.Database.Migrate();
                Console.WriteLine("Database migration completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database migration failed: {ex.Message}");
            }

            // Enable Swagger for all environments
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "User Service API V1");
                c.RoutePrefix = "swagger";
            });

            app.UseCors("AllowAll");
            app.UseAuthorization();
            app.MapControllers();

            // Root endpoint
            app.MapGet("/", () => new { 
                service = "UserService", 
                status = "running",
                platform = "Render.com",
                swagger = "/swagger",
                timestamp = DateTime.UtcNow 
            });

            var port = Environment.GetEnvironmentVariable("PORT") ?? "8081";
            Console.WriteLine($"UserService starting on port {port}");

            app.Run($"http://0.0.0.0:{port}");
        }
    }
}
