namespace OrderService.Services;

/// <summary>
/// No-operation cache service used as fallback when Redis is not available
/// </summary>
public class NoOpCacheService : IRedisCacheService
{
    private readonly ILogger<NoOpCacheService> _logger;

    public NoOpCacheService(ILogger<NoOpCacheService> logger)
    {
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        _logger.LogDebug("NoOpCacheService: Cache get operation skipped for key: {Key}", key);
        return Task.FromResult(default(T));
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        _logger.LogDebug("NoOpCacheService: Cache set operation skipped for key: {Key}", key);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _logger.LogDebug("NoOpCacheService: Cache remove operation skipped for key: {Key}", key);
        return Task.CompletedTask;
    }

    public Task RemovePatternAsync(string pattern)
    {
        _logger.LogDebug("NoOpCacheService: Cache remove pattern operation skipped for pattern: {Pattern}", pattern);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key)
    {
        _logger.LogDebug("NoOpCacheService: Cache exists operation skipped for key: {Key}", key);
        return Task.FromResult(false);
    }
}