using Microsoft.AspNetCore.Mvc;
using OrderService.Services;
using System.ComponentModel.DataAnnotations;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthenticationService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a user and returns a JWT token
    /// </summary>
    /// <param name="request">Login credentials</param>
    /// <returns>JWT token for authenticated user</returns>
    /// <response code="200">Authentication successful</response>
    /// <response code="400">Invalid credentials</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), 200)]
    [ProducesResponseType(typeof(string), 400)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Login attempt for user: {Username}", request.Username);

        // In a real application, you would validate against a user database
        // For demo purposes, we'll use simple validation
        var (isValid, userId, role) = await ValidateUserCredentials(request.Username, request.Password);

        if (!isValid)
        {
            _logger.LogWarning("Failed login attempt for user: {Username}", request.Username);
            return BadRequest("Invalid username or password");
        }

        var token = _authService.GenerateJwtToken(userId, request.Username, role);

        _logger.LogInformation("User {Username} logged in successfully with role {Role}", request.Username, role);

        return Ok(new LoginResponseDto
        {
            Token = token,
            Username = request.Username,
            Role = role,
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60)
        });
    }

    private async Task<(bool IsValid, int UserId, string Role)> ValidateUserCredentials(string username, string password)
    {
        // Simulate database lookup - In production, hash passwords and query database
        await Task.Delay(10); // Simulate async database call

        return username.ToLower() switch
        {
            "customer1" when password == "password123" => (true, 1, "Customer"),
            "customer2" when password == "password123" => (true, 2, "Customer"),
            "delivery1" when password == "delivery123" => (true, 101, "DeliveryPartner"),
            "admin" when password == "admin123" => (true, 999, "Admin"),
            _ => (false, 0, "")
        };
    }
}

/// <summary>
/// Login request data transfer object
/// </summary>
public class LoginRequestDto
{
    /// <summary>
    /// Username for authentication
    /// </summary>
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password for authentication
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Login response data transfer object
/// </summary>
public class LoginResponseDto
{
    /// <summary>
    /// JWT authentication token
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Authenticated username
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// User role (Customer, DeliveryPartner, Admin)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// User ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Token expiration time
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}