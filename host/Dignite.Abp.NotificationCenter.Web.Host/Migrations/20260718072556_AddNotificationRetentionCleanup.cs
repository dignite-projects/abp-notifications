using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Abp.NotificationCenter.Web.Host.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRetentionCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                table: "AbpNotifications",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RetentionDeletionTime",
                table: "AbpNotifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpUserNotifications_State_CreationTime",
                table: "AbpUserNotifications",
                columns: new[] { "State", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpUserNotifications_TenantId_NotificationId",
                table: "AbpUserNotifications",
                columns: new[] { "TenantId", "NotificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpUserNotifications_TenantId_State_CreationTime",
                table: "AbpUserNotifications",
                columns: new[] { "TenantId", "State", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotifications_CreationTime",
                table: "AbpNotifications",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotifications_TenantId_CreationTime",
                table: "AbpNotifications",
                columns: new[] { "TenantId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotifications_TenantId_RetentionDeletionTime_CreationTime",
                table: "AbpNotifications",
                columns: new[] { "TenantId", "RetentionDeletionTime", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_State_CompletedTime",
                table: "AbpNotificationDeliveries",
                columns: new[] { "State", "CompletedTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_TenantKey_NotificationId",
                table: "AbpNotificationDeliveries",
                columns: new[] { "TenantKey", "NotificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_TenantKey_State_CompletedTime",
                table: "AbpNotificationDeliveries",
                columns: new[] { "TenantKey", "State", "CompletedTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpUserNotifications_State_CreationTime",
                table: "AbpUserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AbpUserNotifications_TenantId_NotificationId",
                table: "AbpUserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AbpUserNotifications_TenantId_State_CreationTime",
                table: "AbpUserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotifications_CreationTime",
                table: "AbpNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotifications_TenantId_CreationTime",
                table: "AbpNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotifications_TenantId_RetentionDeletionTime_CreationTime",
                table: "AbpNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationDeliveries_State_CompletedTime",
                table: "AbpNotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationDeliveries_TenantKey_NotificationId",
                table: "AbpNotificationDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationDeliveries_TenantKey_State_CompletedTime",
                table: "AbpNotificationDeliveries");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "AbpNotifications");

            migrationBuilder.DropColumn(
                name: "RetentionDeletionTime",
                table: "AbpNotifications");
        }
    }
}
