using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using OrderService.Idempotency;
using OrderService.Model;
using OrderService.Services;

namespace OrderService.Tests.Idempotency;

public class IdempotencyFilterTests
{
    private readonly Mock<IRedisCacheService> _cacheMock = new();
    private readonly Mock<ILogger<IdempotencyFilter>> _loggerMock = new();
    private readonly IdempotencyFilter _filter;

    public IdempotencyFilterTests()
    {
        _filter = new IdempotencyFilter(_cacheMock.Object, _loggerMock.Object);
    }

    // ── Helpers ──

    private static HttpContext CreateHttpContext(string? idempotencyKey = null, int userId = 1)
    {
        var httpContext = new DefaultHttpContext();

        if (idempotencyKey != null)
        {
            httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        var claims = new List<Claim>
        {
            new("userId", userId.ToString()),
            new(ClaimTypes.Name, "testuser"),
            new(ClaimTypes.Role, "Customer")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        return httpContext;
    }

    private static ActionExecutingContext CreateExecutingContext(HttpContext httpContext)
    {
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            new object());
    }

    private static ActionExecutionDelegate CreateNextDelegate(HttpContext httpContext, IActionResult result)
    {
        return () =>
        {
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var executedContext = new ActionExecutedContext(
                actionContext,
                new List<IFilterMetadata>(),
                new object())
            {
                Result = result
            };
            return Task.FromResult(executedContext);
        };
    }

    // ─────────────────────────────────────────────
    // 1. Missing Idempotency-Key header → 400
    // ─────────────────────────────────────────────
    [Fact]
    public async Task MissingHeader_ReturnsBadRequest()
    {
        var httpContext = CreateHttpContext(idempotencyKey: null);
        var context = CreateExecutingContext(httpContext);

        await _filter.OnActionExecutionAsync(context, () =>
        {
            Assert.Fail("Action should not execute when header is missing");
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 2. Empty / whitespace header → 400
    // ─────────────────────────────────────────────
    [Fact]
    public async Task EmptyHeader_ReturnsBadRequest()
    {
        var httpContext = CreateHttpContext(idempotencyKey: "   ");
        var context = CreateExecutingContext(httpContext);

        await _filter.OnActionExecutionAsync(context, () =>
        {
            Assert.Fail("Action should not execute when header is whitespace");
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        context.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 3. First request — executes action + caches result
    // ─────────────────────────────────────────────
    [Fact]
    public async Task FirstRequest_ExecutesActionAndCachesResult()
    {
        var httpContext = CreateHttpContext(idempotencyKey: "key-123");
        var context = CreateExecutingContext(httpContext);

        _cacheMock.Setup(c => c.GetAsync<IdempotencyRecord>(It.IsAny<string>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        var order = new Order
        {
            Id = 1,
            Item = "Paneer Tikka",
            Status = "Placed",
            UserId = 1,
            CustomerName = "Test",
            CreatedAt = DateTime.UtcNow
        };
        var next = CreateNextDelegate(httpContext, new OkObjectResult(order));

        await _filter.OnActionExecutionAsync(context, next);

        // Should not short-circuit — action executed
        context.Result.Should().BeNull();

        // Should cache the response
        _cacheMock.Verify(c => c.SetAsync(
            It.Is<string>(k => k.Contains("idempotency:") && k.Contains("key-123")),
            It.Is<IdempotencyRecord>(r => r.StatusCode == 200 && r.Body!.Contains("Paneer Tikka")),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    // ─────────────────────────────────────────────
    // 4. Duplicate key — replays cached response
    // ─────────────────────────────────────────────
    [Fact]
    public async Task DuplicateKey_ReturnsCachedResponseWithReplayHeader()
    {
        var httpContext = CreateHttpContext(idempotencyKey: "key-123");
        var context = CreateExecutingContext(httpContext);

        var cachedRecord = new IdempotencyRecord
        {
            StatusCode = 200,
            Body = JsonSerializer.Serialize(new { Id = 1, Item = "Paneer Tikka" }),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _cacheMock.Setup(c => c.GetAsync<IdempotencyRecord>(
                It.Is<string>(k => k.Contains("key-123"))))
            .ReturnsAsync(cachedRecord);

        await _filter.OnActionExecutionAsync(context, () =>
        {
            Assert.Fail("Action should not execute on duplicate key");
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        // Should return cached response
        var result = context.Result.Should().BeOfType<ContentResult>().Subject;
        result.StatusCode.Should().Be(200);
        result.Content.Should().Contain("Paneer Tikka");
        result.ContentType.Should().Be("application/json");

        // Should set replay header
        httpContext.Response.Headers["X-Idempotency-Replay"].ToString().Should().Be("true");
    }

    // ─────────────────────────────────────────────
    // 5. Failed request (5xx) — does NOT cache
    // ─────────────────────────────────────────────
    [Fact]
    public async Task FailedRequest_DoesNotCacheResult()
    {
        var httpContext = CreateHttpContext(idempotencyKey: "key-fail");
        var context = CreateExecutingContext(httpContext);

        _cacheMock.Setup(c => c.GetAsync<IdempotencyRecord>(It.IsAny<string>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        var next = CreateNextDelegate(httpContext, new ObjectResult("Server error") { StatusCode = 500 });

        await _filter.OnActionExecutionAsync(context, next);

        // Should NOT cache error responses
        _cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<IdempotencyRecord>(),
            It.IsAny<TimeSpan?>()), Times.Never);
    }

    // ─────────────────────────────────────────────
    // 6. Different users, same key — treated separately
    // ─────────────────────────────────────────────
    [Fact]
    public async Task DifferentUsers_SameKey_TreatedAsSeparateRequests()
    {
        // User 1 has a cached response for "shared-key"
        _cacheMock.Setup(c => c.GetAsync<IdempotencyRecord>(
                It.Is<string>(k => k == "idempotency:1:shared-key")))
            .ReturnsAsync(new IdempotencyRecord { StatusCode = 200, Body = "{}", CreatedAt = DateTime.UtcNow });

        // User 2 with the same "shared-key" — cache miss
        _cacheMock.Setup(c => c.GetAsync<IdempotencyRecord>(
                It.Is<string>(k => k == "idempotency:2:shared-key")))
            .ReturnsAsync((IdempotencyRecord?)null);

        var httpContext = CreateHttpContext(idempotencyKey: "shared-key", userId: 2);
        var context = CreateExecutingContext(httpContext);
        var next = CreateNextDelegate(httpContext, new OkObjectResult(new { Id = 99 }));

        await _filter.OnActionExecutionAsync(context, next);

        // User 2's request should NOT be short-circuited
        context.Result.Should().BeNull();

        // Should cache under user 2's key
        _cacheMock.Verify(c => c.SetAsync(
            It.Is<string>(k => k == "idempotency:2:shared-key"),
            It.IsAny<IdempotencyRecord>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }
}
