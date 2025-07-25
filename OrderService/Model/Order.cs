namespace OrderService.Model
{
    public class Order
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public string Item { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UserId { get; set; }
        public string Status { get; set; }
        public double? DeliveryLatitude { get; set; }   
        public double? DeliveryLongitude { get; set; }  
    }

}
