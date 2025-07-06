namespace OrderService.Events
{
    public class OrderPlacedEvent
    {
        public int OrderId { get; set; }
        public int UserId { get; set; }
        public string Item { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}