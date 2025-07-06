using Microsoft.AspNetCore.Mvc;
using OrderService.Data;
using OrderService.Model;

namespace OrderService.Controllers;

[ApiController]
[Route("[controller]")]
public class OrderController : ControllerBase
{
	private readonly OrderDbContext _context;

	public OrderController(OrderDbContext context)
	{
		_context = context;
	}
	[HttpGet]
    public async Task<IActionResult> Get()
    {
		return Ok();
    }

    [HttpPost("place")]
	public async Task<IActionResult> PlaceOrder([FromBody] Order order)
	{
		_context.Orders.Add(order);
		await _context.SaveChangesAsync();
		return Ok(order);
	}

	[HttpGet("track/{id}")]
	public async Task<IActionResult> TrackOrder(int id)
	{
		var order = await _context.Orders.FindAsync(id);
		return order == null ? NotFound() : Ok(order);
	}
}
