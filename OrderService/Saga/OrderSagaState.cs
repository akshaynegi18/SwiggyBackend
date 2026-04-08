using MassTransit;

namespace OrderService.Saga;

/// <summary>
/// Persisted state for the Order Fulfillment Saga.
/// Tracked per order, correlated by <see cref="OrderId"/>.
/// </summary>
public class OrderSagaState : SagaStateMachineInstance
{
    /// <summary>MassTransit-required primary key.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Current state name (e.g. "PaymentPending", "Completed").</summary>
    public string CurrentState { get; set; } = default!;

    // ── Order snapshot ──
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public string Item { get; set; } = default!;
    public decimal Amount { get; set; }
    public double DestinationLatitude { get; set; }
    public double DestinationLongitude { get; set; }

    // ── Collected during flow ──
    public string? TransactionId { get; set; }
    public int? EstimatedPrepTimeMinutes { get; set; }
    public string? DeliveryPartnerName { get; set; }
    public int? EstimatedDeliveryMinutes { get; set; }
    public string? FailureReason { get; set; }

    // ── Timeout tokens (used by MassTransit to cancel scheduled timeouts) ──
    public Guid? PaymentTimeoutTokenId { get; set; }
    public Guid? RestaurantTimeoutTokenId { get; set; }
    public Guid? DeliveryTimeoutTokenId { get; set; }

    // ── Audit ──
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}