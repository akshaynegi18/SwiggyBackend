using MassTransit;
using OrderService.Events;
using OrderService.Saga.Contracts;

namespace OrderService.Saga;

/// <summary>
/// Orchestrates the Order Fulfillment flow:
///   Placed → Payment → Restaurant Confirmation → Delivery Assignment → Completed
/// with compensating refunds on failure at any stage.
/// </summary>
public class OrderFulfillmentSaga : MassTransitStateMachine<OrderSagaState>
{
    // ── States ──
    public State PaymentPending { get; private set; } = default!;
    public State RestaurantConfirmationPending { get; private set; } = default!;
    public State DeliveryAssignmentPending { get; private set; } = default!;
    public State Refunding { get; private set; } = default!;
    public State Completed { get; private set; } = default!;
    public State Cancelled { get; private set; } = default!;

    // ── Events ──
    public Event<OrderPlacedEvent> OrderPlaced { get; private set; } = default!;
    public Event<PaymentCompleted> PaymentSucceeded { get; private set; } = default!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = default!;
    public Event<RestaurantAccepted> RestaurantConfirmed { get; private set; } = default!;
    public Event<RestaurantRejected> RestaurantRejected { get; private set; } = default!;
    public Event<DeliveryPartnerAssigned> DeliveryAssigned { get; private set; } = default!;
    public Event<DeliveryPartnerNotAvailable> DeliveryNotAvailable { get; private set; } = default!;
    public Event<RefundCompleted> RefundDone { get; private set; } = default!;

    // ── Timeout Schedules ──
    public Schedule<OrderSagaState, PaymentTimeout> PaymentTimeoutSchedule { get; private set; } = default!;
    public Schedule<OrderSagaState, RestaurantTimeout> RestaurantTimeoutSchedule { get; private set; } = default!;
    public Schedule<OrderSagaState, DeliveryTimeout> DeliveryTimeoutSchedule { get; private set; } = default!;

    public OrderFulfillmentSaga()
    {
        InstanceState(x => x.CurrentState);

        // ── Correlation ──
        // Using query expression overload because CorrelateBy<T> requires T : class,
        // and OrderId is int (a value type).
        Event(() => OrderPlaced, e =>
        {
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId);
            e.SelectId(_ => NewId.NextGuid());
            e.InsertOnInitial = true;
        });

        Event(() => PaymentSucceeded, e =>
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId));
        Event(() => PaymentFailed, e =>
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId));
        Event(() => RestaurantConfirmed, e =>
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId));
        Event(() => RestaurantRejected, e =>
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId));
        Event(() => DeliveryAssigned, e =>
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId));
        Event(() => DeliveryNotAvailable, e =>
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId));
        Event(() => RefundDone, e =>
            e.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.OrderId));

        // ── Timeout Schedules ──
        Schedule(() => PaymentTimeoutSchedule, state => state.PaymentTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(5);
            s.Received = e => e.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => RestaurantTimeoutSchedule, state => state.RestaurantTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(5);
            s.Received = e => e.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => DeliveryTimeoutSchedule, state => state.DeliveryTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(3);
            s.Received = e => e.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        // ── Flow ──

        // 1) Order placed → send payment command + schedule payment timeout
        Initially(
            When(OrderPlaced)
                .Then(context =>
                {
                    var msg = context.Message;
                    context.Saga.OrderId = msg.OrderId;
                    context.Saga.UserId = msg.UserId;
                    context.Saga.Item = msg.Item;
                    context.Saga.Amount = CalculateAmount(msg.Item);
                    context.Saga.DestinationLatitude = msg.DestinationLatitude;
                    context.Saga.DestinationLongitude = msg.DestinationLongitude;
                    context.Saga.CreatedAt = DateTime.UtcNow;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Send(new Uri("queue:process-payment"),
                    context => new ProcessPayment
                    {
                        OrderId = context.Saga.OrderId,
                        UserId = context.Saga.UserId,
                        Item = context.Saga.Item,
                        Amount = context.Saga.Amount
                    })
                .Schedule(PaymentTimeoutSchedule,
                    context => new PaymentTimeout { CorrelationId = context.Saga.CorrelationId })
                .TransitionTo(PaymentPending)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "PaymentPending",
                    OrderStatus = "Payment Processing",
                    Details = $"Processing payment of ₹{context.Saga.Amount} for {context.Saga.Item}",
                    Timestamp = DateTime.UtcNow
                }));

        // 2) Payment step — success, failure, or timeout
        During(PaymentPending,
            When(PaymentSucceeded)
                .Unschedule(PaymentTimeoutSchedule)
                .Then(context =>
                {
                    context.Saga.TransactionId = context.Message.TransactionId;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Send(new Uri("queue:confirm-restaurant"),
                    context => new ConfirmRestaurant
                    {
                        OrderId = context.Saga.OrderId,
                        Item = context.Saga.Item
                    })
                .Schedule(RestaurantTimeoutSchedule,
                    context => new RestaurantTimeout { CorrelationId = context.Saga.CorrelationId })
                .TransitionTo(RestaurantConfirmationPending)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "RestaurantConfirmationPending",
                    OrderStatus = "Payment Confirmed",
                    Details = $"Payment successful (TxnId: {context.Saga.TransactionId}). Waiting for restaurant confirmation.",
                    Timestamp = DateTime.UtcNow
                }),

            When(PaymentFailed)
                .Unschedule(PaymentTimeoutSchedule)
                .Then(context =>
                {
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .TransitionTo(Cancelled)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "Cancelled",
                    OrderStatus = "Cancelled",
                    Details = $"Payment failed: {context.Saga.FailureReason}",
                    Timestamp = DateTime.UtcNow
                }),

            When(PaymentTimeoutSchedule!.Received)
                .Then(context =>
                {
                    context.Saga.FailureReason = "Payment timed out after 60 seconds";
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .TransitionTo(Cancelled)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "Cancelled",
                    OrderStatus = "Cancelled",
                    Details = "Payment timed out. Order cancelled.",
                    Timestamp = DateTime.UtcNow
                }));

        // 3) Restaurant step — accept, reject, or timeout
        During(RestaurantConfirmationPending,
            When(RestaurantConfirmed)
                .Unschedule(RestaurantTimeoutSchedule)
                .Then(context =>
                {
                    context.Saga.EstimatedPrepTimeMinutes = context.Message.EstimatedPrepTimeMinutes;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Send(new Uri("queue:assign-delivery-partner"),
                    context => new AssignDeliveryPartner
                    {
                        OrderId = context.Saga.OrderId,
                        DestinationLatitude = context.Saga.DestinationLatitude,
                        DestinationLongitude = context.Saga.DestinationLongitude
                    })
                .Schedule(DeliveryTimeoutSchedule,
                    context => new DeliveryTimeout { CorrelationId = context.Saga.CorrelationId })
                .TransitionTo(DeliveryAssignmentPending)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "DeliveryAssignmentPending",
                    OrderStatus = "Restaurant Confirmed",
                    Details = $"Restaurant accepted. Prep time: ~{context.Saga.EstimatedPrepTimeMinutes} min. Finding delivery partner.",
                    Timestamp = DateTime.UtcNow
                }),

            When(RestaurantRejected)
                .Unschedule(RestaurantTimeoutSchedule)
                .Then(context =>
                {
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Send(new Uri("queue:refund-payment"),
                    context => new RefundPayment
                    {
                        OrderId = context.Saga.OrderId,
                        UserId = context.Saga.UserId,
                        Reason = $"Restaurant rejected: {context.Message.Reason}"
                    })
                .TransitionTo(Refunding)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "Refunding",
                    OrderStatus = "Refunding",
                    Details = "Restaurant rejected order. Initiating refund.",
                    Timestamp = DateTime.UtcNow
                }),

            When(RestaurantTimeoutSchedule!.Received)
                .Then(context =>
                {
                    context.Saga.FailureReason = "Restaurant did not respond within 5 minutes";
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Send(new Uri("queue:refund-payment"),
                    context => new RefundPayment
                    {
                        OrderId = context.Saga.OrderId,
                        UserId = context.Saga.UserId,
                        Reason = "Restaurant confirmation timed out"
                    })
                .TransitionTo(Refunding)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "Refunding",
                    OrderStatus = "Refunding",
                    Details = "Restaurant did not respond in time. Initiating refund.",
                    Timestamp = DateTime.UtcNow
                }));

        // 4) Delivery step — assigned, unavailable, or timeout
        During(DeliveryAssignmentPending,
            When(DeliveryAssigned)
                .Unschedule(DeliveryTimeoutSchedule)
                .Then(context =>
                {
                    context.Saga.DeliveryPartnerName = context.Message.PartnerName;
                    context.Saga.EstimatedDeliveryMinutes = context.Message.EstimatedDeliveryMinutes;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "Completed",
                    OrderStatus = "Out for Delivery",
                    Details = $"Delivery partner {context.Saga.DeliveryPartnerName} assigned. ETA: ~{context.Saga.EstimatedDeliveryMinutes} min.",
                    Timestamp = DateTime.UtcNow
                })
                .TransitionTo(Completed)
                .Finalize(),

            When(DeliveryNotAvailable)
                .Unschedule(DeliveryTimeoutSchedule)
                .Then(context =>
                {
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Send(new Uri("queue:refund-payment"),
                    context => new RefundPayment
                    {
                        OrderId = context.Saga.OrderId,
                        UserId = context.Saga.UserId,
                        Reason = $"Delivery unavailable: {context.Message.Reason}"
                    })
                .TransitionTo(Refunding)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "Refunding",
                    OrderStatus = "Refunding",
                    Details = "No delivery partner available. Initiating refund.",
                    Timestamp = DateTime.UtcNow
                }),

            When(DeliveryTimeoutSchedule!.Received)
                .Then(context =>
                {
                    context.Saga.FailureReason = "No delivery partner found within 3 minutes";
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Send(new Uri("queue:refund-payment"),
                    context => new RefundPayment
                    {
                        OrderId = context.Saga.OrderId,
                        UserId = context.Saga.UserId,
                        Reason = "Delivery partner assignment timed out"
                    })
                .TransitionTo(Refunding)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "Refunding",
                    OrderStatus = "Refunding",
                    Details = "No delivery partner found in time. Initiating refund.",
                    Timestamp = DateTime.UtcNow
                }));

        // 5) Refund completed → saga cancelled
        During(Refunding,
            When(RefundDone)
                .Then(context => context.Saga.UpdatedAt = DateTime.UtcNow)
                .TransitionTo(Cancelled)
                .Publish(context => new OrderFulfillmentStatusChanged
                {
                    OrderId = context.Saga.OrderId,
                    SagaState = "Cancelled",
                    OrderStatus = "Cancelled - Refunded",
                    Details = "Order cancelled. Refund processed successfully.",
                    Timestamp = DateTime.UtcNow
                }));

        SetCompletedWhenFinalized();
    }

    /// <summary>
    /// Simulated pricing — replace with real pricing service.
    /// </summary>
    private static decimal CalculateAmount(string item)
    {
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["Biryani"] = 250m,
            ["Pizza"] = 350m,
            ["Burger"] = 180m,
            ["Dosa"] = 120m,
            ["Pasta"] = 280m,
            ["Noodles"] = 200m,
            ["Thali"] = 220m,
        };

        return prices.TryGetValue(item, out var price) ? price : 199m;
    }
}