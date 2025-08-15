using StackExchange.Redis;
using System.Text.Json;

namespace OrderService.Services;

public class RedisCacheService : IRedisCacheService
{
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer connectionMultiplexer, ILogger<RedisCacheService> logger)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Attempted to get cache value with null or empty key");
            return default(T);
        }

        try
        {
            // Check if connection is still active
            if (!_connectionMultiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection is not active for key: {Key}", key);
                return default(T);
            }

            var value = await _database.StringGetAsync(key);
            if (!value.HasValue)
            {
                _logger.LogDebug("Cache miss for key: {Key}", key);
                return default(T);
            }

            _logger.LogDebug("Cache hit for key: {Key}", key);
            
            // Use JsonSerializer with options for better compatibility
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
            
            return JsonSerializer.Deserialize<T>(value!, options);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failed while retrieving key: {Key}", key);
            return default(T);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timeout while retrieving key: {Key}", key);
            return default(T);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON deserialization failed for key: {Key}", key);
            return default(T);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving from cache for key: {Key}", key);
            return default(T);
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Attempted to set cache value with null or empty key");
            return;
        }

        if (value == null)
        {
            _logger.LogWarning("Attempted to cache null value for key: {Key}", key);
            return;
        }

        try
        {
            // Check if connection is still active
            if (!_connectionMultiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection is not active, skipping cache set for key: {Key}", key);
                return;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
            
            var serializedValue = JsonSerializer.Serialize(value, options);
            await _database.StringSetAsync(key, serializedValue, expiry);
            _logger.LogDebug("Cached value for key: {Key} with expiry: {Expiry}", key, expiry);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failed while setting key: {Key}", key);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timeout while setting key: {Key}", key);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization failed for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error setting cache for key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Attempted to remove cache value with null or empty key");
            return;
        }

        try
        {
            if (!_connectionMultiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection is not active, skipping cache removal for key: {Key}", key);
                return;
            }

            var result = await _database.KeyDeleteAsync(key);
            if (result)
            {
                _logger.LogDebug("Removed cache for key: {Key}", key);
            }
            else
            {
                _logger.LogDebug("Key not found for removal: {Key}", key);
            }
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failed while removing key: {Key}", key);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timeout while removing key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error removing cache for key: {Key}", key);
        }
    }

    public async Task RemovePatternAsync(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            _logger.LogWarning("Attempted to remove cache values with null or empty pattern");
            return;
        }

        try
        {
            if (!_connectionMultiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection is not active, skipping pattern removal for pattern: {Pattern}", pattern);
                return;
            }

            // Get server endpoint safely
            var endpoints = _connectionMultiplexer.GetEndPoints();
            if (!endpoints.Any())
            {
                _logger.LogWarning("No Redis endpoints available for pattern removal: {Pattern}", pattern);
                return;
            }

            var server = _connectionMultiplexer.GetServer(endpoints.First());
            if (!server.IsConnected)
            {
                _logger.LogWarning("Redis server is not connected for pattern removal: {Pattern}", pattern);
                return;
            }

            var keys = server.Keys(pattern: pattern);
            var keyArray = keys.ToArray();
            
            if (keyArray.Length > 0)
            {
                await _database.KeyDeleteAsync(keyArray);
                _logger.LogDebug("Removed {Count} cache entries matching pattern: {Pattern}", keyArray.Length, pattern);
            }
            else
            {
                _logger.LogDebug("No cache entries found matching pattern: {Pattern}", pattern);
            }
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failed while removing pattern: {Pattern}", pattern);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timeout while removing pattern: {Pattern}", pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error removing cache entries for pattern: {Pattern}", pattern);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Attempted to check cache existence with null or empty key");
            return false;
        }

        try
        {
            if (!_connectionMultiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection is not active, returning false for key existence: {Key}", key);
                return false;
            }

            return await _database.KeyExistsAsync(key);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failed while checking existence for key: {Key}", key);
            return false;
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timeout while checking existence for key: {Key}", key);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking cache existence for key: {Key}", key);
            return false;
        }
    }
}