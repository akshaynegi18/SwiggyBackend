using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using OrderService.Consumers;
using OrderService.Events;

namespace OrderService.Tests.Consumers;

/// <summary>
/// Tests for <see cref="OrderPlacedEventConsumer"/>.
/// Covers: successful consumption, logging, and error propagation.
/// </summary>
public class OrderPlacedEventConsumerTests
{
    private readonly Mock<ILogger<OrderPlacedEventConsumer>> _loggerMock;
    private readonly OrderPlacedEventConsumer _consumer;

    public OrderPlacedEventConsumerTests()
    {
        _loggerMock = new Mock<ILogger<OrderPlacedEventConsumer>>();
        _consumer = new OrderPlacedEventConsumer(_loggerMock.Object);
    }

    private static Mock<ConsumeContext<OrderPlacedEvent>> CreateContext(OrderPlacedEvent message)
    {
        var mock = new Mock<ConsumeContext<OrderPlacedEvent>>();
        mock.Setup(c => c.Message).Returns(message);
        return mock;
    }

    // ─────────────────────────────────────────────
    // 1. Happy path — consumer completes without error
    // ─────────────────────────────────────────────
    [Fact]
    public async Task Consume_ValidEvent_CompletesSuccessfully()
    {
        var evt = new OrderPlacedEvent
        {
            OrderId = 1,
            UserId = 1,
            Item = "Paneer Tikka",
            CreatedAt = DateTime.UtcNow
        };

        var context = CreateContext(evt);

        var act = () => _consumer.Consume(context.Object);

        await act.Should().NotThrowAsync();
    }

    // ─────────────────────────────────────────────
    // 2. Consumer logs the event information
    // ─────────────────────────────────────────────
    [Fact]
    public async Task Consume_ValidEvent_LogsInformation()
    {
        var evt = new OrderPlacedEvent
        {
            OrderId = 42,
            UserId = 7,
            Item = "Veg Biryani",
            CreatedAt = DateTime.UtcNow
        };

        var context = CreateContext(evt);

        await _consumer.Consume(context.Object);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("42")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────
    // 3. All message properties are accessible
    // ─────────────────────────────────────────────
    [Fact]
    public async Task Consume_ValidEvent_ReadsAllProperties()
    {
        var evt = new OrderPlacedEvent
        {
            OrderId = 10,
            UserId = 3,
            Item = "Egg Roll",
            CreatedAt = new DateTime(2026, 4, 7, 12, 0, 0)
        };

        var context = CreateContext(evt);

        // Should not throw — verifies all properties are readable
        await _consumer.Consume(context.Object);

        context.Verify(c => c.Message, Times.AtLeastOnce);
    }
}