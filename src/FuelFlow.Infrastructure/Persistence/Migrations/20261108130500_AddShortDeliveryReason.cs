using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShortDeliveryReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortDeliveryReason",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShortDeliveryReason",
                table: "orders");
        }
    }
}
