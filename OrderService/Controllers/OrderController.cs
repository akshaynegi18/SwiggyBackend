using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OrderService.Data;
using OrderService.Model;
using MassTransit;
using OrderService.Events;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrderController : ControllerBase
{
    private readonly OrderDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPublishEndpoint _publishEndpoint;

    public OrderController(OrderDbContext context, IHttpClientFactory httpClientFactory, IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _publishEndpoint = publishEndpoint;
    }
	
    [HttpPost("place")]
    public async Task<IActionResult> PlaceOrder([FromBody] Order order)
    {
        // Validate user by calling User Service
        var client = _httpClientFactory.CreateClient();
        var userServiceUrl = $"http://user-api:8081/user/{order.UserId}";
        var response = await client.GetAsync(userServiceUrl);

        if (!response.IsSuccessStatusCode)
        {
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

        return Ok(order);
    }

	[HttpGet("track/{id}")]
	public async Task<IActionResult> TrackOrder(int id)
	{
		var order = await _context.Orders.FindAsync(id);
		return order == null ? NotFound() : Ok(order);
	}
}