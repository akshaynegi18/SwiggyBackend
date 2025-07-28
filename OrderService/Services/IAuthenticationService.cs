using System.Security.Claims;

namespace OrderService.Services;

public interface IAuthenticationService
{
    string GenerateJwtToken(int userId, string userName, string role);
    ClaimsPrincipal? ValidateToken(string token);
}