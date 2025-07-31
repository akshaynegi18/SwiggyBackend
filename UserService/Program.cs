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
            builder.Services.AddSwaggerGen();

            

            builder.Services.AddDbContext<UserDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            var app = builder.Build();

            // Apply database migrations with retry logic
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
                            db.Database.Migrate();
                    }

            // Configure the HTTP request pipeline.
           
                app.UseSwagger();
                app.UseSwaggerUI();


                app.UseHttpsRedirection();

            app.UseAuthorization();

           
            app.MapControllers();

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("UserService starting in {Environment} environment on ports 8081", app.Environment.EnvironmentName);

            app.Run();
        }
    }
}
