using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OrderService.Services;

namespace OrderService.Idempotency;

/// <summary>
/// Action filter that enforces idempotency using a client-supplied Idempotency-Key header.
/// On the first request the response is cached in Redis with a 24-hour TTL.
/// Subsequent requests with the same key (scoped per user) replay the cached response.
/// </summary>
public class IdempotencyFilter : IAsyncActionFilter
{
    private readonly IRedisCacheService _cache;
    private readonly ILogger<IdempotencyFilter> _logger;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public IdempotencyFilter(IRedisCacheService cache, ILogger<IdempotencyFilter> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 1. Require Idempotency-Key header
        if (!context.HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue)
            || string.IsNullOrWhiteSpace(headerValue))
        {
            context.Result = new BadRequestObjectResult(new
            {
                error = "Missing Idempotency-Key",
                message = "The Idempotency-Key header is required for this request."
            });
            return;
        }

        var idempotencyKey = headerValue.ToString().Trim();

        // 2. Scope the key to the authenticated user to prevent cross-user collisions
        var userId = context.HttpContext.User.FindFirst("userId")?.Value ?? "anonymous";
        var cacheKey = CacheKeys.GetIdempotencyKey(userId, idempotencyKey);

        // 3. Check for a cached response
        var cached = await _cache.GetAsync<IdempotencyRecord>(cacheKey);
        if (cached != null)
        {
            _logger.LogInformation(
                "Idempotency replay — Key: {Key}, User: {UserId}, OriginalTime: {CreatedAt}",
                idempotencyKey, userId, cached.CreatedAt);

            context.HttpContext.Response.Headers["X-Idempotency-Replay"] = "true";
            context.Result = new ContentResult
            {
                StatusCode = cached.StatusCode,
                Content = cached.Body,
                ContentType = "application/json"
            };
            return;
        }

        // 4. Execute the action
        var executedContext = await next();

        // 5. Cache successful responses (2xx) for replay
        if (executedContext.Result is ObjectResult objectResult
            && (objectResult.StatusCode is null or (>= 200 and < 300)))
        {
            var record = new IdempotencyRecord
            {
                StatusCode = objectResult.StatusCode ?? 200,
                Body = JsonSerializer.Serialize(objectResult.Value),
                CreatedAt = DateTime.UtcNow
            };

            await _cache.SetAsync(cacheKey, record, DefaultTtl);

            _logger.LogDebug(
                "Idempotency stored — Key: {Key}, User: {UserId}, Status: {StatusCode}",
                idempotencyKey, userId, record.StatusCode);
        }
    }
}

/// <summary>
/// Marks an action as idempotent. The client must supply an Idempotency-Key header;
/// duplicate keys (per user) replay the original response instead of re-executing.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class IdempotentRequestAttribute : TypeFilterAttribute
{
    public IdempotentRequestAttribute() : 
        base(typeof(IdempotencyFilter)) { }
}
