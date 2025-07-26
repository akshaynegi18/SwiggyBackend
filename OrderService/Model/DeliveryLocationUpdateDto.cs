using System.ComponentModel.DataAnnotations;

namespace OrderService.Model;

/// <summary>
/// Data transfer object for updating delivery location
/// </summary>
public class DeliveryLocationUpdateDto
{
    /// <summary>
    /// The ID of the order to update
    /// </summary>
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "OrderId must be a positive integer")]
    public int OrderId { get; set; }

    /// <summary>
    /// Current delivery latitude
    /// </summary>
    [Required]
    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90")]
    public double Latitude { get; set; }

    /// <summary>
    /// Current delivery longitude
    /// </summary>
    [Required]
    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180")]
    public double Longitude { get; set; }
}