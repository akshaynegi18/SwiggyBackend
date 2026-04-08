namespace OrderService.Idempotency;

/// <summary>
/// Stores the result of an idempotent request for replay on duplicate keys.
/// </summary>
public class IdempotencyRecord
{
    public int StatusCode { get; set; }
    public string? Body { get; set; }
    public DateTime CreatedAt { get; set; }
}
