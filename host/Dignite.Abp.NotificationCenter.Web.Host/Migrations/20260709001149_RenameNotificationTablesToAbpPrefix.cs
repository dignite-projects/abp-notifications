using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Abp.NotificationCenter.Web.Host.Migrations
{
    /// <inheritdoc />
    public partial class RenameNotificationTablesToAbpPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_NtfUserNotifications",
                table: "NtfUserNotifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NtfNotificationSubscriptions",
                table: "NtfNotificationSubscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NtfNotifications",
                table: "NtfNotifications");

            migrationBuilder.RenameTable(
                name: "NtfUserNotifications",
                newName: "AbpUserNotifications");

            migrationBuilder.RenameTable(
                name: "NtfNotificationSubscriptions",
                newName: "AbpNotificationSubscriptions");

            migrationBuilder.RenameTable(
                name: "NtfNotifications",
                newName: "AbpNotifications");

            migrationBuilder.RenameIndex(
                name: "IX_NtfUserNotifications_UserId_NotificationId",
                table: "AbpUserNotifications",
                newName: "IX_AbpUserNotifications_UserId_NotificationId");

            migrationBuilder.RenameIndex(
                name: "IX_NtfUserNotifications_TenantId_UserId_State_CreationTime",
                table: "AbpUserNotifications",
                newName: "IX_AbpUserNotifications_TenantId_UserId_State_CreationTime");

            migrationBuilder.RenameIndex(
                name: "IX_NtfNotificationSubscriptions_UserId_NotificationName",
                table: "AbpNotificationSubscriptions",
                newName: "IX_AbpNotificationSubscriptions_UserId_NotificationName");

            migrationBuilder.RenameIndex(
                name: "IX_NtfNotificationSubscriptions_TenantId_NotificationName_EntityTypeName_EntityId",
                table: "AbpNotificationSubscriptions",
                newName: "IX_AbpNotificationSubscriptions_TenantId_NotificationName_EntityTypeName_EntityId");

            migrationBuilder.RenameIndex(
                name: "IX_NtfNotifications_TenantId_NotificationName_CreationTime",
                table: "AbpNotifications",
                newName: "IX_AbpNotifications_TenantId_NotificationName_CreationTime");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbpUserNotifications",
                table: "AbpUserNotifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbpNotificationSubscriptions",
                table: "AbpNotificationSubscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AbpNotifications",
                table: "AbpNotifications",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AbpUserNotifications",
                table: "AbpUserNotifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AbpNotificationSubscriptions",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AbpNotifications",
                table: "AbpNotifications");

            migrationBuilder.RenameTable(
                name: "AbpUserNotifications",
                newName: "NtfUserNotifications");

            migrationBuilder.RenameTable(
                name: "AbpNotificationSubscriptions",
                newName: "NtfNotificationSubscriptions");

            migrationBuilder.RenameTable(
                name: "AbpNotifications",
                newName: "NtfNotifications");

            migrationBuilder.RenameIndex(
                name: "IX_AbpUserNotifications_UserId_NotificationId",
                table: "NtfUserNotifications",
                newName: "IX_NtfUserNotifications_UserId_NotificationId");

            migrationBuilder.RenameIndex(
                name: "IX_AbpUserNotifications_TenantId_UserId_State_CreationTime",
                table: "NtfUserNotifications",
                newName: "IX_NtfUserNotifications_TenantId_UserId_State_CreationTime");

            migrationBuilder.RenameIndex(
                name: "IX_AbpNotificationSubscriptions_UserId_NotificationName",
                table: "NtfNotificationSubscriptions",
                newName: "IX_NtfNotificationSubscriptions_UserId_NotificationName");

            migrationBuilder.RenameIndex(
                name: "IX_AbpNotificationSubscriptions_TenantId_NotificationName_EntityTypeName_EntityId",
                table: "NtfNotificationSubscriptions",
                newName: "IX_NtfNotificationSubscriptions_TenantId_NotificationName_EntityTypeName_EntityId");

            migrationBuilder.RenameIndex(
                name: "IX_AbpNotifications_TenantId_NotificationName_CreationTime",
                table: "NtfNotifications",
                newName: "IX_NtfNotifications_TenantId_NotificationName_CreationTime");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NtfUserNotifications",
                table: "NtfUserNotifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NtfNotificationSubscriptions",
                table: "NtfNotificationSubscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NtfNotifications",
                table: "NtfNotifications",
                column: "Id");
        }
    }
}
