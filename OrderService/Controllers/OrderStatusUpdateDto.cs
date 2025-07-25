public class OrderStatusUpdateDto
{
    public int OrderId { get; set; }
    public string Status { get; set; }
    // Optionally, add location fields for delivery tracking:
    // public double? Latitude { get; set; }
    // public double? Longitude { get; set; }
}