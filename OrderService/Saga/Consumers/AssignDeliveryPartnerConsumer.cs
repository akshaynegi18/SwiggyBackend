using MassTransit;
using OrderService.Data;
using OrderService.Saga.Contracts;

namespace OrderService.Saga.Consumers;

public class AssignDeliveryPartnerConsumer : IConsumer<AssignDeliveryPartner>
{
    private readonly ILogger<AssignDeliveryPartnerConsumer> _logger;
    private readonly OrderDbContext _dbContext;

    public AssignDeliveryPartnerConsumer(ILogger<AssignDeliveryPartnerConsumer> logger, OrderDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<AssignDeliveryPartner> context)
    {
        var msg = context.Message;
        _logger.LogInformation(
            "Finding delivery partner for OrderId: {OrderId}, Destination: ({Lat}, {Lng})",
            msg.OrderId, msg.DestinationLatitude, msg.DestinationLongitude);

        var order = await _dbContext.Orders.FindAsync(msg.OrderId);
        if (order != null)
        {
            order.Status = "FindingDeliveryPartner";
            await _dbContext.SaveChangesAsync();
        }

        // TODO: Replace with real delivery partner matching / geolocation logic
        await Task.Delay(2000); // Simulate partner search
        var partnerAvailable = true;
        var partnerNames = new[] { "Rahul", "Priya", "Suresh", "Ananya", "Vikram" };

        if (partnerAvailable)
        {
            var partnerName = partnerNames[Random.Shared.Next(partnerNames.Length)];
            var eta = Random.Shared.Next(20, 45);

            if (order != null)
            {
                order.Status = "OutForDelivery";
                order.ETA = eta;
                await _dbContext.SaveChangesAsync();
            }

            await context.Publish(new DeliveryPartnerAssigned
            {
                OrderId = msg.OrderId,
                PartnerName = partnerName,
                EstimatedDeliveryMinutes = eta
            });

            _logger.LogInformation(
                "Partner {Partner} assigned to OrderId: {OrderId}, ETA: {ETA} min",
                partnerName, msg.OrderId, eta);
        }
        else
        {
            if (order != null)
            {
                order.Status = "NoDeliveryPartner";
                await _dbContext.SaveChangesAsync();
            }

            await context.Publish(new DeliveryPartnerNotAvailable
            {
                OrderId = msg.OrderId,
                Reason = "No delivery partners available in your area"
            });

            _logger.LogWarning("No delivery partner for OrderId: {OrderId}", msg.OrderId);
        }
    }
}