using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OrderService.Migrations
{
    /// <inheritdoc />
    public partial class SeedOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Orders",
                columns: new[] { "Id", "CreatedAt", "CustomerName", "Item" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 7, 2, 14, 35, 26, 883, DateTimeKind.Utc).AddTicks(1437), "Akshay", "Paneer Tikka" },
                    { 2, new DateTime(2025, 7, 2, 14, 35, 26, 883, DateTimeKind.Utc).AddTicks(2820), "Ravi", "Egg Roll" },
                    { 3, new DateTime(2025, 7, 2, 14, 35, 26, 883, DateTimeKind.Utc).AddTicks(2824), "Neha", "Veg Biryani" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Orders",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Orders",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Orders",
                keyColumn: "Id",
                keyValue: 3);
        }
    }
}
