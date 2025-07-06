namespace OrderService.Data
{
    using Microsoft.EntityFrameworkCore;
    using OrderService.Model;

    public class OrderDbContext : DbContext
    {

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Order>().HasData(
                new Order { Id = 1, CustomerName = "Akshay", Item = "Paneer Tikka" },
                new Order { Id = 2, CustomerName = "Ravi", Item = "Egg Roll" },
                new Order { Id = 3, CustomerName = "Neha", Item = "Veg Biryani" }
            );
        }

        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

        public DbSet<Order> Orders { get; set; }
    }

}
