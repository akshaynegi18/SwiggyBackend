using Ocelot.DependencyInjection;
using Ocelot.Middleware;

namespace ApiGateway
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var configFile = env == "Production" ? "ocelot.json" : "ocelot.local.json";
            builder.Configuration.AddJsonFile(configFile, optional: false, reloadOnChange: true);
            builder.Services.AddOcelot();

            var app = builder.Build();

            app.UseOcelot().Wait();

            app.Run();
        }
    }
}
