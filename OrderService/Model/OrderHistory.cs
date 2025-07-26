using System;

namespace OrderService.Model
{
    public class OrderHistory
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string Status { get; set; }
        public double? DeliveryLatitude { get; set; }
        public double? DeliveryLongitude { get; set; }
        public DateTime Timestamp { get; set; }

        public Order Order { get; set; }
    }
}