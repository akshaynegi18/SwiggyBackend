namespace OrderService.Data
{
    using MassTransit;
    using Microsoft.EntityFrameworkCore;
    using OrderService.Model;
    using OrderService.Saga;
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

            // Saga state table configuration
            modelBuilder.Entity<OrderSagaState>(entity =>
            {
                entity.HasKey(x => x.CorrelationId);
                entity.Property(x => x.CurrentState).HasMaxLength(128);
                entity.Property(x => x.Item).HasMaxLength(256);
                entity.Property(x => x.TransactionId).HasMaxLength(128);
                entity.Property(x => x.DeliveryPartnerName).HasMaxLength(128);
                entity.Property(x => x.FailureReason).HasMaxLength(512);
                entity.HasIndex(x => x.OrderId).IsUnique();
            });

            // ── Transactional Outbox tables ──
            // InboxState  — consumer-side deduplication (prevents re-processing the same message)
            // OutboxMessage — stores serialized messages to be delivered to the transport
            // OutboxState — tracks delivery status per outbox batch
            modelBuilder.AddInboxStateEntity();
            modelBuilder.AddOutboxMessageEntity();
            modelBuilder.AddOutboxStateEntity();
        }

        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderHistory> OrderHistories { get; set; }
        public DbSet<OrderSagaState> OrderSagaStates { get; set; }
    }

}
