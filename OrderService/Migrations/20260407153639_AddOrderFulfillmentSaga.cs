using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFulfillmentSaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderSagaStates",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentState = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Item = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DestinationLatitude = table.Column<double>(type: "float", nullable: false),
                    DestinationLongitude = table.Column<double>(type: "float", nullable: false),
                    TransactionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EstimatedPrepTimeMinutes = table.Column<int>(type: "int", nullable: true),
                    DeliveryPartnerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EstimatedDeliveryMinutes = table.Column<int>(type: "int", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSagaStates", x => x.CorrelationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSagaStates_OrderId",
                table: "OrderSagaStates",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderSagaStates");
        }
    }
}
