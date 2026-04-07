using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OrderService.Configuration;
using OrderService.Services;

namespace OrderService.Tests.Services;

/// <summary>
/// Tests for <see cref="AuthenticationService"/>.
/// Covers: token generation, claim correctness, validation, and expiration.
/// </summary>
public class AuthenticationServiceTests
{
    private readonly JwtSettings _jwtSettings;
    private readonly AuthenticationService _sut;

    public AuthenticationServiceTests()
    {
        _jwtSettings = new JwtSettings
        {
            SecretKey = "ThisIsAVerySecretKeyForTestingPurposesOnly1234!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpirationMinutes = 60
        };

        var logger = new Mock<ILogger<AuthenticationService>>();
        _sut = new AuthenticationService(_jwtSettings, logger.Object);
    }

    // ─────────────────────────────────────────────
    // 1. GenerateJwtToken — returns a non-empty string
    // ─────────────────────────────────────────────
    [Fact]
    public void GenerateJwtToken_ValidInput_ReturnsNonEmptyToken()
    {
        var token = _sut.GenerateJwtToken(1, "testuser", "Customer");

        token.Should().NotBeNullOrWhiteSpace();
    }

    // ─────────────────────────────────────────────
    // 2. Token contains expected claims
    // ─────────────────────────────────────────────
    [Fact]
    public void GenerateJwtToken_ValidInput_ContainsExpectedClaims()
    {
        var token = _sut.GenerateJwtToken(42, "akshay", "Admin");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "42");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "akshay");
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        jwt.Claims.Should().Contain(c => c.Type == "userId" && c.Value == "42");
    }

    // ─────────────────────────────────────────────
    // 3. Token has correct issuer and audience
    // ─────────────────────────────────────────────
    [Fact]
    public void GenerateJwtToken_ValidInput_HasCorrectIssuerAndAudience()
    {
        var token = _sut.GenerateJwtToken(1, "testuser", "Customer");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Issuer.Should().Be("TestIssuer");
        jwt.Audiences.Should().Contain("TestAudience");
    }

    // ─────────────────────────────────────────────
    // 4. Each token gets a unique JTI
    // ─────────────────────────────────────────────
    [Fact]
    public void GenerateJwtToken_CalledTwice_ProducesDifferentJtiClaims()
    {
        var token1 = _sut.GenerateJwtToken(1, "user", "Customer");
        var token2 = _sut.GenerateJwtToken(1, "user", "Customer");

        var handler = new JwtSecurityTokenHandler();
        var jti1 = handler.ReadJwtToken(token1).Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var jti2 = handler.ReadJwtToken(token2).Claims.First(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        jti1.Should().NotBe(jti2);
    }

    // ─────────────────────────────────────────────
    // 5. ValidateToken — valid token returns ClaimsPrincipal
    // ─────────────────────────────────────────────
    [Fact]
    public void ValidateToken_ValidToken_ReturnsClaimsPrincipal()
    {
        var token = _sut.GenerateJwtToken(1, "testuser", "Customer");

        var principal = _sut.ValidateToken(token);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.Name)!.Value.Should().Be("testuser");
    }

    // ─────────────────────────────────────────────
    // 6. ValidateToken — garbage token returns null
    // ─────────────────────────────────────────────
    [Fact]
    public void ValidateToken_InvalidToken_ReturnsNull()
    {
        var principal = _sut.ValidateToken("this.is.not.a.valid.token");

        principal.Should().BeNull();
    }

    // ─────────────────────────────────────────────
    // 7. ValidateToken — expired token returns null
    // ─────────────────────────────────────────────
    [Fact]
    public void ValidateToken_ExpiredToken_ReturnsNull()
    {
        var expiredSettings = new JwtSettings
        {
            SecretKey = _jwtSettings.SecretKey,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            ExpirationMinutes = 0
        };

        var logger = new Mock<ILogger<AuthenticationService>>();
        var svc = new AuthenticationService(expiredSettings, logger.Object);
        var token = svc.GenerateJwtToken(1, "testuser", "Customer");

        // Small delay to ensure token expires (ClockSkew = Zero)
        Thread.Sleep(1100);

        var principal = svc.ValidateToken(token);
        principal.Should().BeNull();
    }

    // ─────────────────────────────────────────────
    // 8. ValidateToken — token signed with wrong key returns null
    // ─────────────────────────────────────────────
    [Fact]
    public void ValidateToken_TokenFromDifferentKey_ReturnsNull()
    {
        var otherSettings = new JwtSettings
        {
            SecretKey = "ACompletelyDifferentSecretKeyThatIsLongEnough!!",
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            ExpirationMinutes = 60
        };

        var logger = new Mock<ILogger<AuthenticationService>>();
        var otherSvc = new AuthenticationService(otherSettings, logger.Object);
        var foreignToken = otherSvc.GenerateJwtToken(1, "hacker", "Admin");

        // Validate with the original service (different key)
        var principal = _sut.ValidateToken(foreignToken);

        principal.Should().BeNull();
    }
}