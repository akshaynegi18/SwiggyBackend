using Microsoft.AspNetCore.SignalR;

namespace OrderService.Hubs
{
    public class OrderTrackingHub : Hub
    {
        // Clients can join a group for their orderId to receive updates
        public async Task JoinOrderGroup(int orderId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"order-{orderId}");
        }
    }
}