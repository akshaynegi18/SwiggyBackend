using System.ComponentModel.DataAnnotations;

// Add data annotations to your DTOs
public class OrderStatusUpdateDto
{
    [Required]
    [Range(1, int.MaxValue)]
    public int OrderId { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Status { get; set; }
    
    // Optionally, add location fields for delivery tracking:
    // public double? Latitude { get; set; }
    // public double? Longitude { get; set; }
}