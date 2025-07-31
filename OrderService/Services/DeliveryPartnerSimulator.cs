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
    private readonly ILogger<DeliveryPartnerSimulator> _logger;

    // Simulated delivery routes for different areas
    private readonly (double lat, double lng)[] _route = new[]
    {
        (28.6139, 77.2090), // Start - Restaurant area
        (28.6145, 77.2100), // Moving towards destination
        (28.6150, 77.2110), // Midway point
        (28.6155, 77.2115), // Almost there
        (28.6160, 77.2120), // Destination area
    };

    // Default destination for orders without destination coordinates
    private readonly (double lat, double lng) _defaultDestination = (28.6160, 77.2120);

    public DeliveryPartnerSimulator(IServiceProvider serviceProvider, IHubContext<OrderTrackingHub> hubContext, ILogger<DeliveryPartnerSimulator> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Delivery Partner Simulator started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

                // Get all active orders (not delivered or cancelled)
                var orders = await db.Orders
                    .Where(o => o.Status != "Delivered" && o.Status != "Cancelled")
                    .ToListAsync(stoppingToken);

                _logger.LogInformation("Processing {OrderCount} active orders", orders.Count);

                foreach (var order in orders)
                {
                    await SimulateDeliveryProgress(order, db, stoppingToken);
                }

                if (orders.Any())
                {
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Delivery Partner Simulator is shutting down");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Delivery Partner Simulator");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Update every 5 seconds for faster simulation
        }
    }

    private async Task SimulateDeliveryProgress(Order order, OrderDbContext db, CancellationToken stoppingToken)
    {
        var random = new Random();

        // Set default destination if not set
        if (!order.DestinationLatitude.HasValue || !order.DestinationLongitude.HasValue)
        {
            order.DestinationLatitude = _defaultDestination.lat;
            order.DestinationLongitude = _defaultDestination.lng;
            _logger.LogInformation("Set default destination for Order {OrderId}", order.Id);
        }

        // Simulate delivery partner movement based on order status
        switch (order.Status)
        {
            case "Placed":
                // Order just placed, assign delivery partner
                order.Status = "Confirmed";
                order.DeliveryLatitude = _route[0].lat; // Start at restaurant
                order.DeliveryLongitude = _route[0].lng;
                _logger.LogInformation("Order {OrderId} confirmed and delivery partner assigned", order.Id);
                break;

            case "Confirmed":
                // Move to "Preparing" status
                order.Status = "Preparing";
                break;

            case "Preparing":
                // Move to "Out for Delivery"
                order.Status = "Out for Delivery";
                order.DeliveryLatitude = _route[1].lat; // Start moving
                order.DeliveryLongitude = _route[1].lng;
                _logger.LogInformation("Order {OrderId} is now out for delivery", order.Id);
                break;

            case "Out for Delivery":
                // Simulate movement along the route
                var currentRouteIndex = GetCurrentRouteIndex(order.DeliveryLatitude ?? _route[0].lat, order.DeliveryLongitude ?? _route[0].lng);
                var nextRouteIndex = Math.Min(currentRouteIndex + 1, _route.Length - 1);

                if (nextRouteIndex < _route.Length - 1)
                {
                    // Still moving towards destination
                    order.DeliveryLatitude = _route[nextRouteIndex].lat;
                    order.DeliveryLongitude = _route[nextRouteIndex].lng;
                }
                else
                {
                    // Reached destination area, deliver the order
                    if (random.Next(0, 4) == 0) // 25% chance to deliver each cycle
                    {
                        order.Status = "Delivered";
                        order.DeliveryLatitude = order.DestinationLatitude;
                        order.DeliveryLongitude = order.DestinationLongitude;
                        order.ETA = 0;
                        _logger.LogInformation("Order {OrderId} has been delivered!", order.Id);
                    }
                    else
                    {
                        // Almost there, fine-tune position
                        order.DeliveryLatitude = order.DestinationLatitude + (random.NextDouble() - 0.5) * 0.001;
                        order.DeliveryLongitude = order.DestinationLongitude + (random.NextDouble() - 0.5) * 0.001;
                    }
                }
                break;

            case "Delivered":
                // Order completed, no further simulation needed
                return;
        }

        // Calculate ETA if order is still active
        if (order.Status != "Delivered" && order.DeliveryLatitude.HasValue && order.DeliveryLongitude.HasValue)
        {
            order.ETA = CalculateEta(
                order.DeliveryLatitude.Value,
                order.DeliveryLongitude.Value,
                order.DestinationLatitude!.Value,
                order.DestinationLongitude!.Value
            );
        }

        // Log status update to OrderHistory
        db.OrderHistories.Add(new OrderHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            DeliveryLatitude = order.DeliveryLatitude,
            DeliveryLongitude = order.DeliveryLongitude,
            Timestamp = DateTime.UtcNow
        });

        // Broadcast update via SignalR
        await _hubContext.Clients.Group($"order-{order.Id}")
            .SendAsync("OrderStatusUpdated", new
            {
                OrderId = order.Id,
                Status = order.Status,
                Latitude = order.DeliveryLatitude,
                Longitude = order.DeliveryLongitude,
                ETA = order.ETA,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "DeliveryPartnerSimulator"
            }, stoppingToken);

        _logger.LogDebug("Order {OrderId} updated - Status: {Status}, ETA: {ETA} minutes", 
            order.Id, order.Status, order.ETA);
    }

    private int GetCurrentRouteIndex(double lat, double lng)
    {
        double minDistance = double.MaxValue;
        int closestIndex = 0;

        for (int i = 0; i < _route.Length; i++)
        {
            var distance = CalculateDistance(lat, lng, _route[i].lat, _route[i].lng);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private double CalculateDistance(double lat1, double lng1, double lat2, double lng2)
    {
        return Math.Sqrt(Math.Pow(lat2 - lat1, 2) + Math.Pow(lng2 - lng1, 2));
    }

    private int CalculateEta(double fromLat, double fromLng, double toLat, double toLng, double avgSpeedKmh = 25)
    {
        const double R = 6371; // Radius of the earth in km
        double dLat = (toLat - fromLat) * Math.PI / 180;
        double dLon = (toLng - fromLng) * Math.PI / 180;
        double a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(fromLat * Math.PI / 180) * Math.Cos(toLat * Math.PI / 180) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        double distance = R * c; // Distance in km

        double etaHours = distance / avgSpeedKmh;
        int etaMinutes = (int)Math.Ceiling(etaHours * 60);
        
        // Ensure realistic ETA between 1-30 minutes
        return Math.Max(1, Math.Min(etaMinutes, 30));
    }
}