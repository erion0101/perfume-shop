using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameShipmentTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The parcel SMS is now sent at handover (ShipmentHandedOver), not when the shipment is booked. Keeps Admin edits.
            migrationBuilder.Sql("""
                UPDATE notifications.templates SET key = 'shipment-handed-over'
                WHERE key = 'shipment-created'
                  AND NOT EXISTS (SELECT 1 FROM notifications.templates WHERE key = 'shipment-handed-over');
                DELETE FROM notifications.templates WHERE key = 'shipment-created';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE notifications.templates SET key = 'shipment-created' WHERE key = 'shipment-handed-over';");
        }
    }
}
