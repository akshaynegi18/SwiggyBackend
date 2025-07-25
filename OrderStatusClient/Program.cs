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

        // Retry logic for connecting to SignalR hub
        var maxAttempts = 10;
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