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
using System.ComponentModel.DataAnnotations;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
[Produces("application/json")]
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
	
    /// <summary>
    /// Places a new order for a customer
    /// </summary>
    /// <param name="order">Order details including customer information and items</param>
    /// <returns>The created order with assigned ID</returns>
    /// <response code="200">Order placed successfully</response>
    /// <response code="400">Invalid order data or user validation failed</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpPost("place")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderDto orderDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Placing order for UserId: {UserId}, Item: {Item}", orderDto.UserId, orderDto.Item);

        try
        {
            // Validate user by calling User Service
            var client = _httpClientFactory.CreateClient();
            var userServiceUrl = $"http://user-api:8081/user/{orderDto.UserId}";
            var response = await client.GetAsync(userServiceUrl);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("User validation failed for UserId: {UserId}, StatusCode: {StatusCode}", 
                    orderDto.UserId, response.StatusCode);
                return BadRequest("Invalid user.");
            }

            var order = new Order
            {
                UserId = orderDto.UserId,
                CustomerName = orderDto.CustomerName,
                Item = orderDto.Item,
                DestinationLatitude = orderDto.DestinationLatitude,
                DestinationLongitude = orderDto.DestinationLongitude,
                CreatedAt = DateTime.UtcNow,
                Status = "Placed"
            };

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
            _logger.LogError(ex, "Error placing order for UserId: {UserId}", orderDto.UserId);
            return StatusCode(500, "An error occurred while placing the order.");
        }
    }

    /// <summary>
    /// Tracks an order by its ID
    /// </summary>
    /// <param name="id">The order ID to track</param>
    /// <returns>Order details with current status and location</returns>
    /// <response code="200">Order found and returned</response>
    /// <response code="404">Order not found</response>
    [HttpGet("track/{id}")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> TrackOrder([Range(1, int.MaxValue)] int id)
    {
        _logger.LogInformation("Tracking order with OrderId: {OrderId}", id);
        var order = await _context.Orders.FindAsync(id);
        
        if (order == null)
        {
            _logger.LogWarning("Order not found. OrderId: {OrderId}", id);
            return NotFound($"Order with ID {id} not found.");
        }

        return Ok(order);
    }

    /// <summary>
    /// Updates the status of an existing order
    /// </summary>
    /// <param name="update">Status update information</param>
    /// <returns>Updated order details</returns>
    /// <response code="200">Order status updated successfully</response>
    /// <response code="400">Invalid update data</response>
    /// <response code="404">Order not found</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpPost("update-status")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 404)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> UpdateOrderStatus([FromBody] OrderStatusUpdateDto update, [FromServices] IHubContext<OrderTrackingHub> hubContext)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Updating order status. OrderId: {OrderId}, NewStatus: {Status}", 
            update.OrderId, update.Status);

        try
        {
            var order = await _context.Orders.FindAsync(update.OrderId);
            if (order == null) 
            {
                _logger.LogWarning("Order not found for status update. OrderId: {OrderId}", update.OrderId);
                return NotFound($"Order with ID {update.OrderId} not found.");
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

    /// <summary>
    /// Updates the delivery location for an order
    /// </summary>
    /// <param name="update">Location update information with coordinates</param>
    /// <returns>Updated order with new ETA calculation</returns>
    /// <response code="200">Location updated successfully</response>
    /// <response code="400">Invalid location data</response>
    /// <response code="404">Order not found</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpPost("update-location")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 404)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> UpdateDeliveryLocation(
        [FromBody] DeliveryLocationUpdateDto update,
        [FromServices] IHubContext<OrderTrackingHub> hubContext)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Updating delivery location. OrderId: {OrderId}, Latitude: {Latitude}, Longitude: {Longitude}", 
            update.OrderId, update.Latitude, update.Longitude);

        try
        {
            var order = await _context.Orders.FindAsync(update.OrderId);
            if (order == null) 
            {
                _logger.LogWarning("Order not found for location update. OrderId: {OrderId}", update.OrderId);
                return NotFound($"Order with ID {update.OrderId} not found.");
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

    /// <summary>
    /// Retrieves the complete timeline/history for an order
    /// </summary>
    /// <param name="orderId">The order ID to get timeline for</param>
    /// <returns>List of order history entries ordered by timestamp</returns>
    /// <response code="200">Timeline retrieved successfully</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("timeline/{orderId}")]
    [ProducesResponseType(typeof(List<OrderHistory>), 200)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> GetOrderTimeline([Range(1, int.MaxValue)] int orderId)
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

    /// <summary>
    /// Gets personalized recommendations for a user based on order history
    /// </summary>
    /// <param name="userId">The user ID to get recommendations for</param>
    /// <returns>List of recommended items with order frequency</returns>
    /// <response code="200">Recommendations retrieved successfully</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("recommendations/{userId}")]
    [ProducesResponseType(typeof(List<RecommendationDto>), 200)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> GetRecommendations([Range(1, int.MaxValue)] int userId)
    {
        _logger.LogInformation("Fetching recommendations for UserId: {UserId}", userId);

        try
        {
            var recommendations = await _context.Orders
                .Where(o => o.UserId == userId)
                .GroupBy(o => o.Item)
                .Select(g => new RecommendationDto
                {
                    Item = g.Key,
                    OrderCount = g.Count(),
                    LastOrderDate = g.Max(o => o.CreatedAt)
                })
                .OrderByDescending(x => x.OrderCount)
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

/// <summary>
/// Data transfer object for placing a new order
/// </summary>
public class PlaceOrderDto
{
    /// <summary>
    /// The ID of the user placing the order
    /// </summary>
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "UserId must be a positive integer")]
    public int UserId { get; set; }

    /// <summary>
    /// Name of the customer
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Customer name must be between 2 and 100 characters")]
    public string CustomerName { get; set; }

    /// <summary>
    /// Item or dish being ordered
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Item name is required and cannot exceed 200 characters")]
    public string Item { get; set; }

    /// <summary>
    /// Delivery destination latitude
    /// </summary>
    [Required]
    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90")]
    public double DestinationLatitude { get; set; }

    /// <summary>
    /// Delivery destination longitude
    /// </summary>
    [Required]
    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180")]
    public double DestinationLongitude { get; set; }
}

/// <summary>
/// Data transfer object for recommendation results
/// </summary>
public class RecommendationDto
{
    /// <summary>
    /// Name of the recommended item
    /// </summary>
    public string Item { get; set; }

    /// <summary>
    /// Number of times this item was ordered
    /// </summary>
    public int OrderCount { get; set; }

    /// <summary>
    /// Date of the last order for this item
    /// </summary>
    public DateTime LastOrderDate { get; set; }
}