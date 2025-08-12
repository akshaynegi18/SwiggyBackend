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
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
[Produces("application/json")]
[Authorize] // Require authentication for all endpoints
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
    /// <param name="orderDto">Order details including customer information and items</param>
    /// <returns>The created order with assigned ID</returns>
    /// <response code="200">Order placed successfully</response>
    /// <response code="400">Invalid order data or user validation failed</response>
    /// <response code="401">Unauthorized - JWT token required</response>
    /// <response code="403">Forbidden - Customer role required</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpPost("place")]
    [Authorize(Policy = "CustomerOnly")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderDto orderDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Get authenticated user information
        var authenticatedUserId = GetAuthenticatedUserId();
        var authenticatedUsername = User.Identity?.Name ?? "Unknown";

        // Security check: Users can only place orders for themselves
        if (orderDto.UserId != authenticatedUserId)
        {
            _logger.LogWarning("User {Username} (ID: {AuthUserId}) attempted to place order for different user ID: {RequestedUserId}", 
                authenticatedUsername, authenticatedUserId, orderDto.UserId);
            return Forbid("You can only place orders for yourself.");
        }

        _logger.LogInformation("Authenticated user {Username} placing order for UserId: {UserId}, Item: {Item}", 
            authenticatedUsername, orderDto.UserId, orderDto.Item);

        try
        {
            // Validate user by calling User Service (optional since user is already authenticated)
            var httpClient = _httpClientFactory.CreateClient("UserService");
            var userResponse = await httpClient.GetAsync($"/users/{orderDto.UserId}");

            if (!userResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("User validation failed for UserId: {UserId}. StatusCode: {StatusCode}", orderDto.UserId, userResponse.StatusCode);
                return BadRequest("User does not exist or could not be validated.");
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

            _logger.LogInformation("Order placed successfully by {Username}. OrderId: {OrderId}, UserId: {UserId}", 
                authenticatedUsername, order.Id, order.UserId);

            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error placing order for authenticated user {Username}, UserId: {UserId}", 
                authenticatedUsername, orderDto.UserId);
            return StatusCode(500, "An error occurred while placing the order.");
        }
    }

    /// <summary>
    /// Tracks an order by its ID
    /// </summary>
    /// <param name="id">The order ID to track</param>
    /// <returns>Order details with current status and location</returns>
    /// <response code="200">Order found and returned</response>
    /// <response code="401">Unauthorized - JWT token required</response>
    /// <response code="403">Forbidden - You can only track your own orders</response>
    /// <response code="404">Order not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("track/{id}")]
    [Authorize(Policy = "CustomerOrAdmin")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> TrackOrder([Range(1, int.MaxValue)] int id)
    {
        var authenticatedUsername = "Unknown";
        var authenticatedUserId = 0;
        var userRole = "Unknown";
        
        try
        {
            _logger.LogInformation("Starting order tracking request for OrderId: {OrderId}", id);
            
            authenticatedUserId = GetAuthenticatedUserId();
            authenticatedUsername = User.Identity?.Name ?? "Unknown";
            userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown";

            _logger.LogInformation("User {Username} (ID: {UserId}, Role: {Role}) tracking order with OrderId: {OrderId}", 
                authenticatedUsername, authenticatedUserId, userRole, id);

            var order = await _context.Orders.FindAsync(id);
            
            if (order == null)
            {
                _logger.LogWarning("Order not found. OrderId: {OrderId}, RequestedBy: {Username} (ID: {UserId})", 
                    id, authenticatedUsername, authenticatedUserId);
                return NotFound($"Order with ID {id} not found.");
            }

            _logger.LogInformation("Order found. OrderId: {OrderId}, OrderUserId: {OrderUserId}, OrderStatus: {Status}, RequestedBy: {Username}", 
                id, order.UserId, order.Status, authenticatedUsername);

            // Security check: Users can only track their own orders (unless they're admin)
            if (userRole != "Admin" && order.UserId != authenticatedUserId)
            {
                _logger.LogWarning("Unauthorized order tracking attempt. User {Username} (ID: {AuthUserId}, Role: {Role}) attempted to track order {OrderId} belonging to user {OrderUserId}", 
                    authenticatedUsername, authenticatedUserId, userRole, id, order.UserId);
                return Forbid("You can only track your own orders.");
            }

            _logger.LogInformation("Order tracking successful. OrderId: {OrderId}, RequestedBy: {Username}, OrderStatus: {Status}, ETA: {ETA}", 
                id, authenticatedUsername, order.Status, order.ETA);

            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking order. OrderId: {OrderId}, RequestedBy: {Username} (ID: {UserId}, Role: {Role}), ErrorType: {ErrorType}, ErrorMessage: {ErrorMessage}", 
                id, authenticatedUsername, authenticatedUserId, userRole, ex.GetType().Name, ex.Message);
            
            // Log additional details for database-related errors
            if (ex is InvalidOperationException || ex.InnerException != null)
            {
                _logger.LogError("Additional error details - InnerException: {InnerException}, StackTrace: {StackTrace}", 
                    ex.InnerException?.Message ?? "None", ex.StackTrace);
            }
            
            return StatusCode(500, "An error occurred while tracking the order.");
        }
    }

    /// <summary>
    /// Updates the status of an existing order (Admin or Delivery Partner only)
    /// </summary>
    /// <param name="update">Status update information</param>
    /// <param name="hubContext">SignalR hub context for broadcasting updates</param>
    /// <returns>Updated order details</returns>
    /// <response code="200">Order status updated successfully</response>
    /// <response code="400">Invalid update data</response>
    /// <response code="401">Unauthorized - JWT token required</response>
    /// <response code="403">Forbidden - Admin or DeliveryPartner role required</response>
    /// <response code="404">Order not found</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpPost("update-status")]
    [Authorize(Roles = "Admin,DeliveryPartner")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(string), 404)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> UpdateOrderStatus([FromBody] OrderStatusUpdateDto update, [FromServices] IHubContext<OrderTrackingHub> hubContext)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var authenticatedUsername = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

        _logger.LogInformation("User {Username} (Role: {Role}) updating order status. OrderId: {OrderId}, NewStatus: {Status}", 
            authenticatedUsername, userRole, update.OrderId, update.Status);

        try
        {
            var order = await _context.Orders.FindAsync(update.OrderId);
            if (order == null) 
            {
                _logger.LogWarning("Order not found for status update. OrderId: {OrderId}, RequestedBy: {Username}", 
                    update.OrderId, authenticatedUsername);
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
                    UpdatedBy = authenticatedUsername,
                    UpdatedAt = DateTime.UtcNow
                });

            _logger.LogInformation("Order status updated successfully by {Username}. OrderId: {OrderId}, OldStatus: {OldStatus}, NewStatus: {NewStatus}", 
                authenticatedUsername, order.Id, oldStatus, order.Status);

            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status. OrderId: {OrderId}, RequestedBy: {Username}", 
                update.OrderId, authenticatedUsername);
            return StatusCode(500, "An error occurred while updating order status.");
        }
    }

    /// <summary>
    /// Updates the delivery location for an order (Delivery Partner only)
    /// </summary>
    /// <param name="update">Location update information with coordinates</param>
    /// <param name="hubContext">SignalR hub context for broadcasting updates</param>
    /// <returns>Updated order with new ETA calculation</returns>
    /// <response code="200">Location updated successfully</response>
    /// <response code="400">Invalid location data</response>
    /// <response code="401">Unauthorized - JWT token required</response>
    /// <response code="403">Forbidden - DeliveryPartner role required</response>
    /// <response code="404">Order not found</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpPost("update-location")]
    [Authorize(Policy = "DeliveryPartnerOnly")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
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

        var authenticatedUsername = User.Identity?.Name ?? "Unknown";

        _logger.LogInformation("Delivery partner {Username} updating location. OrderId: {OrderId}, Latitude: {Latitude}, Longitude: {Longitude}", 
            authenticatedUsername, update.OrderId, update.Latitude, update.Longitude);

        try
        {
            var order = await _context.Orders.FindAsync(update.OrderId);
            if (order == null) 
            {
                _logger.LogWarning("Order not found for location update. OrderId: {OrderId}, RequestedBy: {Username}", 
                    update.OrderId, authenticatedUsername);
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
                    ETA = order.ETA,
                    UpdatedBy = authenticatedUsername,
                    UpdatedAt = DateTime.UtcNow
                });

            _logger.LogInformation("Delivery location updated successfully by {Username}. OrderId: {OrderId}, ETA: {ETA} minutes", 
                authenticatedUsername, order.Id, order.ETA);

            return Ok(order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating delivery location. OrderId: {OrderId}, RequestedBy: {Username}", 
                update.OrderId, authenticatedUsername);
            return StatusCode(500, "An error occurred while updating delivery location.");
        }
    }

    /// <summary>
    /// Retrieves the complete timeline/history for an order
    /// </summary>
    /// <param name="orderId">The order ID to get timeline for</param>
    /// <returns>List of order history entries ordered by timestamp</returns>
    /// <response code="200">Timeline retrieved successfully</response>
    /// <response code="401">Unauthorized - JWT token required</response>
    /// <response code="403">Forbidden - You can only view timeline for your own orders</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("timeline/{orderId}")]
    [Authorize(Policy = "CustomerOrAdmin")]
    [ProducesResponseType(typeof(List<OrderHistory>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> GetOrderTimeline([Range(1, int.MaxValue)] int orderId)
    {
        var authenticatedUserId = GetAuthenticatedUserId();
        var authenticatedUsername = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

        _logger.LogInformation("User {Username} fetching order timeline. OrderId: {OrderId}", authenticatedUsername, orderId);

        try
        {
            // First check if order exists and user has permission
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null)
            {
                return NotFound($"Order with ID {orderId} not found.");
            }

            // Security check: Users can only view timeline for their own orders (unless they're admin)
            if (userRole != "Admin" && order.UserId != authenticatedUserId)
            {
                _logger.LogWarning("User {Username} (ID: {AuthUserId}) attempted to view timeline for order {OrderId} belonging to user {OrderUserId}", 
                    authenticatedUsername, authenticatedUserId, orderId, order.UserId);
                return Forbid("You can only view timeline for your own orders.");
            }

            var history = await _context.OrderHistories
                .Where(h => h.OrderId == orderId)
                .OrderBy(h => h.Timestamp)
                .ToListAsync();

            _logger.LogInformation("Order timeline fetched successfully by {Username}. OrderId: {OrderId}, HistoryCount: {Count}", 
                authenticatedUsername, orderId, history.Count);

            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching order timeline. OrderId: {OrderId}, RequestedBy: {Username}", 
                orderId, authenticatedUsername);
            return StatusCode(500, "An error occurred while fetching order timeline.");
        }
    }

    /// <summary>
    /// Gets personalized recommendations for a user based on order history
    /// </summary>
    /// <param name="userId">The user ID to get recommendations for</param>
    /// <returns>List of recommended items with order frequency</returns>
    /// <response code="200">Recommendations retrieved successfully</response>
    /// <response code="401">Unauthorized - JWT token required</response>
    /// <response code="403">Forbidden - You can only get recommendations for yourself</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("recommendations/{userId}")]
    [Authorize(Policy = "CustomerOrAdmin")]
    [ProducesResponseType(typeof(List<RecommendationDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> GetRecommendations([Range(1, int.MaxValue)] int userId)
    {
        var authenticatedUserId = GetAuthenticatedUserId();
        var authenticatedUsername = User.Identity?.Name ?? "Unknown";
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

        // Security check: Users can only get recommendations for themselves (unless they're admin)
        if (userRole != "Admin" && userId != authenticatedUserId)
        {
            _logger.LogWarning("User {Username} (ID: {AuthUserId}) attempted to get recommendations for different user ID: {RequestedUserId}", 
                authenticatedUsername, authenticatedUserId, userId);
            return Forbid("You can only get recommendations for yourself.");
        }

        _logger.LogInformation("User {Username} fetching recommendations for UserId: {UserId}", authenticatedUsername, userId);

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

            _logger.LogInformation("Recommendations fetched successfully by {Username}. UserId: {UserId}, RecommendationCount: {Count}", 
                authenticatedUsername, userId, recommendations.Count);

            return Ok(recommendations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recommendations. UserId: {UserId}, RequestedBy: {Username}", 
                userId, authenticatedUsername);
            return StatusCode(500, "An error occurred while fetching recommendations.");
        }
    }

    /// <summary>
    /// Helper method to get authenticated user ID from JWT claims
    /// </summary>
    private int GetAuthenticatedUserId()
    {
        var userIdClaim = User.FindFirst("userId") ?? User.FindFirst(ClaimTypes.NameIdentifier);
        return int.Parse(userIdClaim?.Value ?? "0");
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