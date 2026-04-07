using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OrderService.Controllers;
using OrderService.Model;
using OrderService.Services;
using OrderService.Tests.Helpers;

namespace OrderService.Tests.Controllers;

/// <summary>
/// Tests for the GET /order/recommendations/{userId} endpoint.
/// Covers: happy path, cache hit, ownership security, admin override, and ranking.
/// </summary>
public class GetRecommendationsTests : IDisposable
{
    private readonly OrderControllerTestFixture _fixture;

    public GetRecommendationsTests()
    {
        _fixture = new OrderControllerTestFixture();
    }

    public void Dispose() => _fixture.Dispose();

    // ── Helper ──
    private void SeedOrders(int userId = 1)
    {
        var orders = new[]
        {
            new Order { UserId = userId, CustomerName = "A", Item = "Paneer Tikka", Status = "Delivered", CreatedAt = DateTime.UtcNow.AddDays(-5) },
            new Order { UserId = userId, CustomerName = "A", Item = "Paneer Tikka", Status = "Delivered", CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new Order { UserId = userId, CustomerName = "A", Item = "Paneer Tikka", Status = "Delivered", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new Order { UserId = userId, CustomerName = "A", Item = "Egg Roll",     Status = "Delivered", CreatedAt = DateTime.UtcNow.AddDays(-4) },
            new Order { UserId = userId, CustomerName = "A", Item = "Egg Roll",     Status = "Delivered", CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new Order { UserId = userId, CustomerName = "A", Item = "Veg Biryani",  Status = "Delivered", CreatedAt = DateTime.UtcNow.AddDays(-6) },
        };
        _fixture.DbContext.Orders.AddRange(orders);
        _fixture.DbContext.SaveChanges();
    }

    // ─────────────────────────────────────────────
    // 1. Happy path — returns recommendations ordered by frequency
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetRecommendations_ValidUser_ReturnsOrderedByFrequency()
    {
        SeedOrders(userId: 1);
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetRecommendations(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var recs = ok.Value.Should().BeAssignableTo<List<RecommendationDto>>().Subject;

        recs.Should().HaveCount(3);
        recs[0].Item.Should().Be("Paneer Tikka");
        recs[0].OrderCount.Should().Be(3);
        recs[1].Item.Should().Be("Egg Roll");
        recs[1].OrderCount.Should().Be(2);
        recs[2].Item.Should().Be("Veg Biryani");
        recs[2].OrderCount.Should().Be(1);
    }

    // ─────────────────────────────────────────────
    // 2. Cache hit — returns cached recommendations
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetRecommendations_CacheHit_ReturnsCachedData()
    {
        var cached = new List<RecommendationDto>
        {
            new() { Item = "Cached Item", OrderCount = 10, LastOrderDate = DateTime.UtcNow }
        };

        _fixture.CacheServiceMock
            .Setup(c => c.GetAsync<List<RecommendationDto>>(CacheKeys.GetRecommendationsKey(1)))
            .ReturnsAsync(cached);

        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetRecommendations(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var recs = ok.Value.Should().BeAssignableTo<List<RecommendationDto>>().Subject;
        recs.Should().HaveCount(1);
        recs[0].Item.Should().Be("Cached Item");
    }

    // ─────────────────────────────────────────────
    // 3. Different user — Forbid
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetRecommendations_DifferentUser_ReturnsForbid()
    {
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetRecommendations(999);

        result.Should().BeOfType<ForbidResult>();
    }

    // ─────────────────────────────────────────────
    // 4. Admin can view any user's recommendations
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetRecommendations_Admin_CanViewAnyUser()
    {
        SeedOrders(userId: 5);
        var controller = _fixture.CreateController(userId: 99, role: "Admin");

        var result = await controller.GetRecommendations(5);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ─────────────────────────────────────────────
    // 5. No order history — returns empty list
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetRecommendations_NoOrders_ReturnsEmptyList()
    {
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetRecommendations(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var recs = ok.Value.Should().BeAssignableTo<List<RecommendationDto>>().Subject;
        recs.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────
    // 6. Recommendations are capped at 5 items
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetRecommendations_ManyItems_ReturnsCappedAt5()
    {
        var items = new[] { "A", "B", "C", "D", "E", "F", "G" };
        foreach (var item in items)
        {
            _fixture.DbContext.Orders.Add(new Order
            {
                UserId = 1,
                CustomerName = "Test",
                Item = item,
                Status = "Delivered",
                CreatedAt = DateTime.UtcNow
            });
        }
        _fixture.DbContext.SaveChanges();

        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetRecommendations(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var recs = ok.Value.Should().BeAssignableTo<List<RecommendationDto>>().Subject;
        recs.Should().HaveCount(5);
    }

    // ─────────────────────────────────────────────
    // 7. Recommendations are cached for 2 hours after fetch
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetRecommendations_CacheMiss_CachesFor2Hours()
    {
        SeedOrders(userId: 1);
        var controller = _fixture.CreateController(userId: 1);

        await controller.GetRecommendations(1);

        _fixture.CacheServiceMock.Verify(
            c => c.SetAsync(
                CacheKeys.GetRecommendationsKey(1),
                It.IsAny<List<RecommendationDto>>(),
                It.Is<TimeSpan?>(t => t!.Value.TotalHours == 2)),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 8. LastOrderDate reflects the most recent order
    // ─────────────────────────────────────────────
    [Fact]
    public async Task GetRecommendations_ValidUser_LastOrderDateIsCorrect()
    {
        SeedOrders(userId: 1);
        var controller = _fixture.CreateController(userId: 1);

        var result = await controller.GetRecommendations(1);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var recs = ok.Value.Should().BeAssignableTo<List<RecommendationDto>>().Subject;

        // Paneer Tikka was most recently ordered 1 day ago
        recs[0].LastOrderDate.Should().BeCloseTo(DateTime.UtcNow.AddDays(-1), TimeSpan.FromSeconds(5));
    }
}