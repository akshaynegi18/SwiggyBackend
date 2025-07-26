using System.ComponentModel.DataAnnotations;

namespace OrderService.Model;

/// <summary>
/// Data transfer object for updating order status
/// </summary>
public class OrderStatusUpdateDto
{
    /// <summary>
    /// The ID of the order to update
    /// </summary>
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "OrderId must be a positive integer")]
    public int OrderId { get; set; }

    /// <summary>
    /// New status for the order (e.g., "Placed", "Preparing", "Out for Delivery", "Delivered")
    /// </summary>
    [Required]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Status is required and cannot exceed 50 characters")]
    [RegularExpression("^(Placed|Preparing|Out for Delivery|Delivered|Cancelled)$", 
        ErrorMessage = "Status must be one of: Placed, Preparing, Out for Delivery, Delivered, Cancelled")]
    public string Status { get; set; }
}