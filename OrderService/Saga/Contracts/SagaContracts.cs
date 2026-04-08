namespace OrderService.Saga.Contracts;

// ────────────────────────────────────────────────
// Commands — sent by the saga TO service consumers
// ────────────────────────────────────────────────

public record ProcessPayment
{
    public int OrderId { get; init; }
    public int UserId { get; init; }
    public string Item { get; init; } = default!;
    public decimal Amount { get; init; }
}

public record ConfirmRestaurant
{
    public int OrderId { get; init; }
    public string Item { get; init; } = default!;
}

public record AssignDeliveryPartner
{
    public int OrderId { get; init; }
    public double DestinationLatitude { get; init; }
    public double DestinationLongitude { get; init; }
}

public record RefundPayment
{
    public int OrderId { get; init; }
    public int UserId { get; init; }
    public string Reason { get; init; } = default!;
}

// ────────────────────────────────────────────────
// Events — published by consumers BACK to the saga
// ────────────────────────────────────────────────

public record PaymentCompleted
{
    public int OrderId { get; init; }
    public string TransactionId { get; init; } = default!;
}

public record PaymentFailed
{
    public int OrderId { get; init; }
    public string Reason { get; init; } = default!;
}

public record RestaurantAccepted
{
    public int OrderId { get; init; }
    public int EstimatedPrepTimeMinutes { get; init; }
}

public record RestaurantRejected
{
    public int OrderId { get; init; }
    public string Reason { get; init; } = default!;
}

public record DeliveryPartnerAssigned
{
    public int OrderId { get; init; }
    public string PartnerName { get; init; } = default!;
    public int EstimatedDeliveryMinutes { get; init; }
}

public record DeliveryPartnerNotAvailable
{
    public int OrderId { get; init; }
    public string Reason { get; init; } = default!;
}

public record RefundCompleted
{
    public int OrderId { get; init; }
}

// ────────────────────────────────────────────────
// Saga status event — published at every transition
// ────────────────────────────────────────────────

public record OrderFulfillmentStatusChanged
{
    public int OrderId { get; init; }
    public string SagaState { get; init; } = default!;
    public string OrderStatus { get; init; } = default!;
    public string? Details { get; init; }
    public DateTime Timestamp { get; init; }
}

// ────────────────────────────────────────────────
// Timeout messages — scheduled by the saga, delivered
// back when a step doesn't respond in time
// ────────────────────────────────────────────────

public record PaymentTimeout
{
    public Guid CorrelationId { get; init; }
}

public record RestaurantTimeout
{
    public Guid CorrelationId { get; init; }
}

public record DeliveryTimeout
{
    public Guid CorrelationId { get; init; }
}