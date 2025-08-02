using Microsoft.EntityFrameworkCore;
using UserService.Data;

namespace UserService
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
                c.SwaggerDoc("v1", new() { Title = "User Service API", Version = "v1" });
            });

            // Configure database context
            builder.Services.AddDbContext<UserDbContext>(options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
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

            // Apply database migrations with retry logic for Azure
            if (!app.Environment.IsDevelopment())
            {
                using (var scope = app.Services.CreateScope())
                {
                    var services = scope.ServiceProvider;
                    var scopedLogger = services.GetRequiredService<ILogger<Program>>(); // Renamed to 'scopedLogger'

                    try
                    {
                        var db = services.GetRequiredService<UserDbContext>();
                        db.Database.Migrate();
                        scopedLogger.LogInformation("Database migration completed successfully");
                    }
                    catch (Exception ex)
                    {
                        scopedLogger.LogError(ex, "Error occurred during database migration");
                        // In production, you might want to implement more sophisticated error handling
                    }
                }
            }

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
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "User Service API v1");
                    c.RoutePrefix = "swagger";
                });
            }

            app.UseCors("AllowAll");
            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();

            // Add health check endpoint
            app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "UserService" }));

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("UserService starting in {Environment} environment", app.Environment.EnvironmentName);

            app.Run();
        }
    }
}
