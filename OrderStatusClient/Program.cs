using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

class Program
{
    static async Task Main(string[] args)
    {
        var orderId = 1; // Set the order ID you want to track
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:8080/order-tracking-hub")
            .WithAutomaticReconnect()
            .Build();

        connection.On<object>("OrderStatusUpdated", update =>
        {
            Console.WriteLine($"Order status update: {update}");
        });

        connection.On<DeliveryLocationUpdate>("DeliveryLocationUpdated", update =>
        {
            Console.WriteLine($"Order {update.OrderId} location: {update.Latitude}, {update.Longitude} | ETA: {update.ETA} min");
        });

        // Retry logic for connecting to SignalR hub
        var maxAttempts = 100;
        var delayMs = 2000;
        var connected = false;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await connection.StartAsync();
                connected = true;
                Console.WriteLine("Connected to SignalR hub.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attempt {attempt}: Failed to connect to SignalR hub. {ex.Message}");
                if (attempt == maxAttempts)
                {
                    Console.WriteLine("Max connection attempts reached. Exiting.");
                    return;
                }
                await Task.Delay(delayMs);
            }
        }

        // Join the group for the order
        await connection.InvokeAsync("JoinOrderGroup", orderId);
        Console.WriteLine($"Joined group for order {orderId}.");

        Console.WriteLine("Listening for status updates. Press Enter to exit.");
        Console.ReadLine();

        await connection.StopAsync();
    }
}

public class DeliveryLocationUpdate
{
    public int OrderId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? ETA { get; set; } // ETA in minutes
}