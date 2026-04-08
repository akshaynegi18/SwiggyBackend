using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaTimeoutTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryTimeoutTokenId",
                table: "OrderSagaStates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentTimeoutTokenId",
                table: "OrderSagaStates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RestaurantTimeoutTokenId",
                table: "OrderSagaStates",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryTimeoutTokenId",
                table: "OrderSagaStates");

            migrationBuilder.DropColumn(
                name: "PaymentTimeoutTokenId",
                table: "OrderSagaStates");

            migrationBuilder.DropColumn(
                name: "RestaurantTimeoutTokenId",
                table: "OrderSagaStates");
        }
    }
}
