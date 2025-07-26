using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OrderService.Data;
using OrderService.Model;
using MassTransit;
using OrderService.Events;
using Microsoft.AspNetCore.SignalR;
using OrderService.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrderController : ControllerBase
{
    private readonly OrderDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        OrderDbContext context, 
        IHttpClientFactory httpClientFactory, 
        IPublishEndpoint publishEndpoint,
        ILogger<OrderController> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }
	
    [HttpPost("place")]
    public async Task<IActionResult> PlaceOrder([FromBody] Order order)
    {
        _logger.LogInformation("Placing order for UserId: {UserId}, Item: {Item}", order.UserId, order.Item);

        try
        {
            // Validate user by calling User Service
            var client = _httpClientFactory.CreateClient();
            var userServiceUrl = $"http://user-api:8081/user/{order.UserId}";
            var response = await client.GetAsync(userServiceUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("User validation failed for UserId: {UserId}, StatusCode: {StatusCode}", 
                    order.UserId, response.StatusCode);
                return BadRequest("Invalid user.");
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Publish event
            var orderPlacedEvent = new OrderPlacedEvent
            {
                OrderId = order.Id,
                UserId = order.UserId,
                Item = order.Item,
                CreatedAt = order.CreatedAt
            };
            await _publishEndpoint.Publish(orderPlacedEvent);

            _logger.LogInformation("Order placed successfully. OrderId: {OrderId}, UserId: {UserId}", 
                order.Id, order.UserId);

            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing order for UserId: {UserId}", order.UserId);
            return StatusCode(500, "An error occurred while placing the order.");
        }
    }

	[HttpGet("track/{id}")]
	public async Task<IActionResult> TrackOrder(int id)
	{
        _logger.LogInformation("Tracking order with OrderId: {OrderId}", id);
		var order = await _context.Orders.FindAsync(id);
        
        if (order == null)
        {
            _logger.LogWarning("Order not found. OrderId: {OrderId}", id);
            return NotFound();
        }

        return Ok(order);
	}

    [HttpPost("update-status")]
    public async Task<IActionResult> UpdateOrderStatus([FromBody] OrderStatusUpdateDto update, [FromServices] IHubContext<OrderTrackingHub> hubContext)
    {
        _logger.LogInformation("Updating order status. OrderId: {OrderId}, NewStatus: {Status}", 
            update.OrderId, update.Status);

        try
        {
            var order = await _context.Orders.FindAsync(update.OrderId);
            if (order == null) 
            {
                _logger.LogWarning("Order not found for status update. OrderId: {OrderId}", update.OrderId);
                return NotFound();
            }

            var oldStatus = order.Status;
            order.Status = update.Status;

            // Log status update to OrderHistory
            _context.OrderHistories.Add(new OrderHistory
            {
                OrderId = order.Id,
                Status = order.Status,
                DeliveryLatitude = order.DeliveryLatitude,
                DeliveryLongitude = order.DeliveryLongitude,
                Timestamp = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // Broadcast status update via SignalR
            await hubContext.Clients.Group($"order-{order.Id}")
                .SendAsync("OrderStatusUpdated", new
                {
                    OrderId = order.Id,
                    Status = order.Status,
                });

            _logger.LogInformation("Order status updated successfully. OrderId: {OrderId}, OldStatus: {OldStatus}, NewStatus: {NewStatus}", 
                order.Id, oldStatus, order.Status);

            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status. OrderId: {OrderId}", update.OrderId);
            return StatusCode(500, "An error occurred while updating order status.");
        }
    }

    [HttpPost("update-location")]
    public async Task<IActionResult> UpdateDeliveryLocation(
        [FromBody] DeliveryLocationUpdateDto update,
        [FromServices] IHubContext<OrderTrackingHub> hubContext)
    {
        _logger.LogInformation("Updating delivery location. OrderId: {OrderId}, Latitude: {Latitude}, Longitude: {Longitude}", 
            update.OrderId, update.Latitude, update.Longitude);

        try
        {
            var order = await _context.Orders.FindAsync(update.OrderId);
            if (order == null) 
            {
                _logger.LogWarning("Order not found for location update. OrderId: {OrderId}", update.OrderId);
                return NotFound();
            }

            order.DeliveryLatitude = update.Latitude;
            order.DeliveryLongitude = update.Longitude;

            order.ETA = CalculateEta(
                update.Latitude,
                update.Longitude,
                Convert.ToDouble(order.DestinationLatitude),
                Convert.ToDouble(order.DestinationLongitude)
            );

            _context.OrderHistories.Add(new OrderHistory
            {
                OrderId = order.Id,
                Status = order.Status,
                DeliveryLatitude = order.DeliveryLatitude,
                DeliveryLongitude = order.DeliveryLongitude,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            // Broadcast location and ETA update via SignalR
            await hubContext.Clients.Group($"order-{order.Id}")
                .SendAsync("DeliveryLocationUpdated", new
                {
                    OrderId = order.Id,
                    Latitude = order.DeliveryLatitude,
                    Longitude = order.DeliveryLongitude,
                    ETA = order.ETA
                });

            _logger.LogInformation("Delivery location updated successfully. OrderId: {OrderId}, ETA: {ETA} minutes", 
                order.Id, order.ETA);

            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating delivery location. OrderId: {OrderId}", update.OrderId);
            return StatusCode(500, "An error occurred while updating delivery location.");
        }
    }

    [HttpGet("timeline/{orderId}")]
    public async Task<IActionResult> GetOrderTimeline(int orderId)
    {
        _logger.LogInformation("Fetching order timeline. OrderId: {OrderId}", orderId);

        try
        {
            var history = await _context.OrderHistories
                .Where(h => h.OrderId == orderId)
                .OrderBy(h => h.Timestamp)
                .ToListAsync();

            _logger.LogInformation("Order timeline fetched successfully. OrderId: {OrderId}, HistoryCount: {Count}", 
                orderId, history.Count);

            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching order timeline. OrderId: {OrderId}", orderId);
            return StatusCode(500, "An error occurred while fetching order timeline.");
        }
    }

    [HttpGet("recommendations/{userId}")]
    public async Task<IActionResult> GetRecommendations(int userId)
    {
        _logger.LogInformation("Fetching recommendations for UserId: {UserId}", userId);

        try
        {
            var recommendations = await _context.Orders
                .Where(o => o.UserId == userId)
                .GroupBy(o => o.Item)
                .Select(g => new
                {
                    Item = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            _logger.LogInformation("Recommendations fetched successfully. UserId: {UserId}, RecommendationCount: {Count}", 
                userId, recommendations.Count);

            return Ok(recommendations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recommendations. UserId: {UserId}", userId);
            return StatusCode(500, "An error occurred while fetching recommendations.");
        }
    }

    private int CalculateEta(double fromLat, double fromLng, double toLat, double toLng, double avgSpeedKmh = 30)
    {
        double R = 6371; // Radius of the earth in km
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
        return etaMinutes;
    }
}