using Microsoft.AspNetCore.Mvc;
using OrderService.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthController> _logger;
    private readonly IConfiguration _configuration;

    public AuthController(
        IAuthenticationService authService, 
        IHttpClientFactory httpClientFactory,
        ILogger<AuthController> logger,
        IConfiguration configuration)
    {
        _authService = authService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Authenticates a user and returns a JWT token
    /// </summary>
    /// <param name="request">Login credentials</param>
    /// <returns>JWT token for authenticated user</returns>
    /// <response code="200">Authentication successful</response>
    /// <response code="400">Invalid credentials</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), 200)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Login attempt for user: {Username}", request.Username);

        try
        {
            // Validate user credentials via UserService
            var (isValid, userId, username, name, role) = await ValidateUserCredentials(request.Username, request.Password);

            if (!isValid)
            {
                _logger.LogWarning("Failed login attempt for user: {Username}", request.Username);
                return BadRequest("Invalid username or password");
            }

            // Generate JWT token
            var token = _authService.GenerateJwtToken(userId, username, role);

            _logger.LogInformation("User {Username} logged in successfully with role {Role}", username, role);

            return Ok(new LoginResponseDto
            {
                Token = token,
                Username = username,
                Name = name,
                Role = role,
                UserId = userId,
                ExpiresAt = DateTime.UtcNow.AddMinutes(60)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
            return StatusCode(500, "An error occurred during authentication");
        }
    }

    /// <summary>
    /// Register a new user account
    /// </summary>
    /// <param name="request">User registration details</param>
    /// <returns>Registration result</returns>
    /// <response code="201">User registered successfully</response>
    /// <response code="400">Invalid registration data</response>
    /// <response code="409">Username or email already exists</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterResponseDto), 201)]
    [ProducesResponseType(typeof(string), 400)]
    [ProducesResponseType(typeof(string), 409)]
    [ProducesResponseType(typeof(string), 500)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Registration attempt for user: {Username}", request.Username);

        try
        {
            var client = _httpClientFactory.CreateClient();
            
            // Get UserService URL from environment variable/configuration
            var userServiceBaseUrl = GetUserServiceUrl();
            var userServiceUrl = $"{userServiceBaseUrl}/user/register";
            
            _logger.LogInformation("Calling UserService at: {UserServiceUrl}", userServiceUrl);

            var userServiceRequest = new
            {
                Username = request.Username,
                Name = request.Name,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                Password = request.Password,
                Role = request.Role ?? "Customer"
            };

            var json = JsonSerializer.Serialize(userServiceRequest);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync(userServiceUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var userResponse = JsonSerializer.Deserialize<UserServiceResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("User {Username} registered successfully", request.Username);

                return CreatedAtAction(nameof(Login), new RegisterResponseDto
                {
                    UserId = userResponse!.Id,
                    Username = userResponse.Username,
                    Name = userResponse.Name,
                    Email = userResponse.Email,
                    Role = userResponse.Role,
                    Message = "User registered successfully"
                });
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogWarning("Registration failed - user already exists: {Username}", request.Username);
                return Conflict("Username or email already exists");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Registration failed for user: {Username}, Error: {Error}", request.Username, errorContent);
                return BadRequest("Registration failed: " + errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for user: {Username}", request.Username);
            return StatusCode(500, "An error occurred during registration");
        }
    }

    private async Task<(bool IsValid, int UserId, string Username, string Name, string Role)> ValidateUserCredentials(string username, string password)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            
            // Get UserService URL from environment variable/configuration
            var userServiceBaseUrl = GetUserServiceUrl();
            var userServiceUrl = $"{userServiceBaseUrl}/user/validate";
            
            _logger.LogInformation("Validating user credentials at: {UserServiceUrl}", userServiceUrl);

            var validateRequest = new
            {
                Username = username,
                Password = password
            };

            var json = JsonSerializer.Serialize(validateRequest);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync(userServiceUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var validationResult = JsonSerializer.Deserialize<UserValidationResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (validationResult != null && validationResult.IsValid)
                {
                    return (true, validationResult.UserId, validationResult.Username, validationResult.Name, validationResult.Role);
                }
            }

            return (false, 0, "", "", "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user credentials for: {Username}", username);
            return (false, 0, "", "", "");
        }
    }

    private string GetUserServiceUrl()
    {
        // Try environment variable first (UserService__BaseUrl)
        var userServiceUrl = Environment.GetEnvironmentVariable("UserService__BaseUrl") 
                           ?? _configuration["UserService:BaseUrl"]
                           ?? "http://user-api:8081"; // Fallback for local development

        // Remove trailing slash if present
        userServiceUrl = userServiceUrl.TrimEnd('/');
        
        _logger.LogInformation("Using UserService URL: {UserServiceUrl}", userServiceUrl);
        return userServiceUrl;
    }
}

// DTOs for the updated AuthController
public class LoginRequestDto
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class RegisterRequestDto
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Phone]
    [StringLength(15)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Role { get; set; }
}

public class RegisterResponseDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

// Internal DTOs for UserService communication
internal class UserValidationResponse
{
    public bool IsValid { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

internal class UserServiceResponse
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}