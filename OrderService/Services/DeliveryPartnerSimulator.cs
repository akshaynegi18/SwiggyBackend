using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using OrderService.Data;
using OrderService.Hubs;
using OrderService.Model;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class DeliveryPartnerSimulator : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<OrderTrackingHub> _hubContext;

    // Simulated path (array of lat/lng points)
    private readonly (double lat, double lng)[] _route = new[]
    {
        (28.6139, 77.2090), // Start
        (28.6145, 77.2100),
        (28.6150, 77.2110),
        (28.6160, 77.2120), // End
    };

    public DeliveryPartnerSimulator(IServiceProvider serviceProvider, IHubContext<OrderTrackingHub> hubContext)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            // Get all active orders (not delivered)
            var orders = await db.Orders
                .Where(o => o.Status != "Delivered")
                .ToListAsync(stoppingToken);

            foreach (var order in orders)
            {
                // Simulate movement: pick a random point from the route
                var point = _route[new Random().Next(_route.Length)];
                order.DeliveryLatitude = point.lat;
                order.DeliveryLongitude = point.lng;
                order.Status = "Out for Delivery";

                // Optionally, recalculate ETA if you have destination info
                if (order.DestinationLatitude.HasValue && order.DestinationLongitude.HasValue)
                {
                    order.ETA = CalculateEta(
                        order.DeliveryLatitude.Value,
                        order.DeliveryLongitude.Value,
                        order.DestinationLatitude.Value,
                        order.DestinationLongitude.Value
                    );
                }

                db.Orders.Update(order);

                // Broadcast location and ETA update via SignalR
                await _hubContext.Clients.Group($"order-{order.Id}")
                    .SendAsync("DeliveryLocationUpdated", new
                    {
                        OrderId = order.Id,
                        Latitude = order.DeliveryLatitude,
                        Longitude = order.DeliveryLongitude,
                        ETA = order.ETA
                    }, stoppingToken);
            }

            await db.SaveChangesAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // Update every 10 seconds
        }
    }

    private int CalculateEta(double fromLat, double fromLng, double toLat, double toLng, double avgSpeedKmh = 30)
    {
        double R = 6371;
        double dLat = (toLat - fromLat) * Math.PI / 180;
        double dLon = (toLng - fromLng) * Math.PI / 180;
        double a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(fromLat * Math.PI / 180) * Math.Cos(toLat * Math.PI / 180) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        double distance = R * c;
        double etaHours = distance / avgSpeedKmh;
        int etaMinutes = (int)Math.Ceiling(etaHours * 60);
        return etaMinutes;
    }
}