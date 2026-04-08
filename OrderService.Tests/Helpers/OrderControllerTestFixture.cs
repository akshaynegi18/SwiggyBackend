using System.Net;
using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OrderService.Controllers;
using OrderService.Data;
using OrderService.Hubs;
using OrderService.Model;
using OrderService.Services;

namespace OrderService.Tests.Helpers;

/// <summary>
/// Shared test fixture that creates all mocks and an in-memory database
/// for OrderController unit tests. Each test gets a fresh instance via IDisposable.
/// </summary>
public class OrderControllerTestFixture : IDisposable
{
    // ── Core dependencies ──
    public OrderDbContext DbContext { get; }
    public Mock<IPublishEndpoint> PublishEndpointMock { get; }
    public Mock<ILogger<OrderController>> LoggerMock { get; }
    public Mock<IRedisCacheService> CacheServiceMock { get; }
    public Mock<IHubContext<OrderTrackingHub>> HubContextMock { get; }
    public IConfiguration Configuration { get; }

    public OrderControllerTestFixture()
    {
        // 1) In-memory database — unique name per instance to avoid cross-test leakage
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrderTestDb_{Guid.NewGuid()}")
            .Options;
        DbContext = new OrderDbContext(options);

        // 2) Mocks
        PublishEndpointMock = new Mock<IPublishEndpoint>();
        LoggerMock = new Mock<ILogger<OrderController>>();
        CacheServiceMock = new Mock<IRedisCacheService>();
        HubContextMock = new Mock<IHubContext<OrderTrackingHub>>();

        // 3) Configuration (cache expiration setting)
        var configData = new Dictionary<string, string?>
        {
            { "Redis:DefaultExpirationMinutes", "30" }
        };
        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // 4) SignalR hub mock — returns a mock IClientProxy for group broadcasts
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        HubContextMock.Setup(h => h.Clients).Returns(mockClients.Object);

        // 5) Cache mock — return null (cache miss) for every concrete type the controller uses.
        //    Moq matches generic type parameters exactly, so GetAsync<object> does NOT
        //    cover GetAsync<Order>. Each type the controller awaits must be set up
        //    individually to return a completed Task; otherwise await receives null and throws.
        CacheServiceMock
            .Setup(c => c.GetAsync<Order>(It.IsAny<string>()))
            .ReturnsAsync((Order?)null);

        CacheServiceMock
            .Setup(c => c.GetAsync<List<OrderHistory>>(It.IsAny<string>()))
            .ReturnsAsync((List<OrderHistory>?)null);

        CacheServiceMock
            .Setup(c => c.GetAsync<List<RecommendationDto>>(It.IsAny<string>()))
            .ReturnsAsync((List<RecommendationDto>?)null);
    }

    /// <summary>
    /// Creates an OrderController with all mocks wired up and the specified user claims.
    /// </summary>
    public OrderController CreateController(int userId = 1, string username = "testuser", string role = "Customer")
    {
        var controller = new OrderController(
            DbContext,
            PublishEndpointMock.Object,
            LoggerMock.Object,
            CacheServiceMock.Object,
            Configuration);

        // Attach a fake authenticated user (simulates JWT claims)
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreateClaimsPrincipal(userId, username, role)
            }
        };

        return controller;
    }

    /// <summary>
    /// Builds a ClaimsPrincipal with userId, name, and role claims — mirrors your JWT structure.
    /// </summary>
    public static ClaimsPrincipal CreateClaimsPrincipal(int userId, string username, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role),
            new("userId", userId.ToString())
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    public void Dispose()
    {
        DbContext.Database.EnsureDeleted();
        DbContext.Dispose();
    }
}