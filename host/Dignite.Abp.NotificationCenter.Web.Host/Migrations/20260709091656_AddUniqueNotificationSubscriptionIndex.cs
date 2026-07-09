using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Abp.NotificationCenter.Web.Host.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueNotificationSubscriptionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationSubscriptions_TenantId_UserId_NotificationName_EntityTypeName_EntityId",
                table: "AbpNotificationSubscriptions",
                columns: new[] { "TenantId", "UserId", "NotificationName", "EntityTypeName", "EntityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationSubscriptions_TenantId_UserId_NotificationName_EntityTypeName_EntityId",
                table: "AbpNotificationSubscriptions");
        }
    }
}
