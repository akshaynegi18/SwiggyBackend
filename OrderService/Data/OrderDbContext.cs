namespace OrderService.Data
{
    using Microsoft.EntityFrameworkCore;
    using OrderService.Model;
    using System;

    public class OrderDbContext : DbContext
    {

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Order>().HasData(
                new Order { Id = 1, CustomerName = "Akshay", Item = "Paneer Tikka", Status = "Placed", CreatedAt = new DateTime(2024, 01, 01, 12, 0, 0), UserId = 1 },
                new Order { Id = 2, CustomerName = "Ravi", Item = "Egg Roll", Status = "Placed", CreatedAt = new DateTime(2024, 01, 01, 12, 5, 0), UserId = 1 },
                new Order { Id = 3, CustomerName = "Neha", Item = "Veg Biryani", Status = "Placed", CreatedAt = new DateTime(2024, 01, 01, 12, 10, 0), UserId = 1 }
            );
        }

        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

        public DbSet<Order> Orders { get; set; }
    }

}
