using System.Net.Http.Json;

namespace OrderService.Resilience;

/// <summary>
/// Typed HttpClient for calling UserService with built-in resilience
/// (retry, circuit breaker, timeout) via Microsoft.Extensions.Http.Resilience.
/// When the circuit breaker opens, calls fail fast instead of waiting for a
/// downstream service that is already struggling — preventing cascade failures.
/// </summary>
public class UserServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserServiceClient> _logger;

    public UserServiceClient(HttpClient httpClient, ILogger<UserServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Validates that a user exists and is active.
    /// Returns a graceful result even when the circuit breaker is open.
    /// </summary>
    public async Task<UserValidationResult> ValidateUserAsync(int userId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Validating user {UserId} via UserService", userId);

            var response = await _httpClient.GetAsync($"/User/{userId}", ct);

            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<UserInfoDto>(cancellationToken: ct);
                return new UserValidationResult
                {
                    IsValid = user is { IsActive: true },
                    Username = user?.Username,
                    ErrorReason = user is { IsActive: false } ? "User account is inactive" : null
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UserValidationResult { IsValid = false, ErrorReason = "User not found" };
            }

            _logger.LogWarning("UserService returned {StatusCode} for user {UserId}",
                response.StatusCode, userId);

            return new UserValidationResult
            {
                IsValid = false,
                ErrorReason = $"UserService error: {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            // This catches circuit breaker open, timeouts, and connection failures.
            // The caller decides whether to fail hard or degrade gracefully.
            _logger.LogError(ex, "Failed to validate user {UserId} — circuit breaker may be open", userId);
            return new UserValidationResult
            {
                IsValid = false,
                ErrorReason = "UserService unavailable",
                IsServiceDown = true
            };
        }
    }

    /// <summary>
    /// Gets basic user info for order enrichment.
    /// Returns null on failure (graceful degradation).
    /// </summary>
    public async Task<UserInfoDto?> GetUserAsync(int userId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Fetching user {UserId} from UserService", userId);
            return await _httpClient.GetFromJsonAsync<UserInfoDto>($"/User/{userId}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch user {UserId} — returning null (degraded mode)", userId);
            return null;
        }
    }
}

public class UserValidationResult
{
    public bool IsValid { get; init; }
    public string? Username { get; init; }
    public string? ErrorReason { get; init; }

    /// <summary>
    /// True when the failure is due to infrastructure (timeout, circuit open)
    /// rather than a definitive "user not found" answer.
    /// Callers can use this to decide whether to fail hard or degrade.
    /// </summary>
    public bool IsServiceDown { get; init; }
}

/// <summary>
/// Lightweight DTO for user data received from UserService.
/// </summary>
public class UserInfoDto
{
    public int Id { get; init; }
    public string Username { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string Email { get; init; } = default!;
    public bool IsActive { get; init; }
}
