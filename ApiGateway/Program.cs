using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

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

            // ── Gateway-level Rate Limiting ──
            // Protects ALL downstream services before requests even reach them.
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)retryAfter.TotalSeconds).ToString();
                    }

                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "Too many requests",
                        message = "Gateway rate limit exceeded. Please try again later.",
                        retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
                            ? (int)ra.TotalSeconds
                            : (int?)null
                    }, cancellationToken);
                };

                // Per-IP fixed window — 100 requests / minute
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 25
                        }));
            });

            var app = builder.Build();

            // Rate limiter runs BEFORE Ocelot — rejected requests never reach downstream services
            app.UseRateLimiter();

            app.UseOcelot().Wait();

            app.Run();
        }
    }
}
