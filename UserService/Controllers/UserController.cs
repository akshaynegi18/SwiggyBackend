using Microsoft.AspNetCore.Mvc;
using UserService.Data;
using UserService.Model;
using Microsoft.EntityFrameworkCore;

namespace UserService.Controllers;

[ApiController]
[Route("[controller]")]
public class UserController : ControllerBase
{
    private readonly UserDbContext _context;

    public UserController(UserDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var users = await _context.Users.ToListAsync();
        return Ok(users);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return Ok(user);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _context.Users.FindAsync(id);
        return user == null ? NotFound() : Ok(user);
    }
}