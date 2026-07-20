using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Abp.NotificationCenter.Web.Host.Migrations
{
    /// <inheritdoc />
    public partial class SubtractNotificationOverengineering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpNotificationDeliveries");

            migrationBuilder.DropTable(
                name: "AbpNotificationDeliveryPreferences");

            migrationBuilder.DropTable(
                name: "AbpNotificationQuietHours");

            migrationBuilder.DropTable(
                name: "AbpNotificationRetentionCleanupCursors");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotifications_TenantId_RetentionDeletionTime_CreationTime",
                table: "AbpNotifications");

            migrationBuilder.DropColumn(
                name: "RetentionDeletionTime",
                table: "AbpNotifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RetentionDeletionTime",
                table: "AbpNotifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AbpNotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ChannelKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CompletedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    DeliveryNotBefore = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EntityTypeName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 89, nullable: false),
                    Intent = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastFailureMessage = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LeaseExpirationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NextAttemptTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NotificationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PreferenceReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Severity = table.Column<byte>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpNotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpNotificationDeliveryPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ChannelKey = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NotificationName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NotificationNameKey = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpNotificationDeliveryPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpNotificationQuietHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpNotificationQuietHours", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpNotificationRetentionCleanupCursors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsTenantScoped = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCreationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecordKind = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantKey = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpNotificationRetentionCleanupCursors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotifications_TenantId_RetentionDeletionTime_CreationTime",
                table: "AbpNotifications",
                columns: new[] { "TenantId", "RetentionDeletionTime", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_State_CompletedTime",
                table: "AbpNotificationDeliveries",
                columns: new[] { "State", "CompletedTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_State_LeaseExpirationTime",
                table: "AbpNotificationDeliveries",
                columns: new[] { "State", "LeaseExpirationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_State_NextAttemptTime",
                table: "AbpNotificationDeliveries",
                columns: new[] { "State", "NextAttemptTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_TenantKey_NotificationId",
                table: "AbpNotificationDeliveries",
                columns: new[] { "TenantKey", "NotificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_TenantKey_NotificationId_UserId_ChannelKey",
                table: "AbpNotificationDeliveries",
                columns: new[] { "TenantKey", "NotificationId", "UserId", "ChannelKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_TenantKey_State_CompletedTime",
                table: "AbpNotificationDeliveries",
                columns: new[] { "TenantKey", "State", "CompletedTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveryPreferences_TenantKey_UserId",
                table: "AbpNotificationDeliveryPreferences",
                columns: new[] { "TenantKey", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveryPreferences_TenantKey_UserId_NotificationNameKey_ChannelKey",
                table: "AbpNotificationDeliveryPreferences",
                columns: new[] { "TenantKey", "UserId", "NotificationNameKey", "ChannelKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationQuietHours_TenantKey_UserId",
                table: "AbpNotificationQuietHours",
                columns: new[] { "TenantKey", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationRetentionCleanupCursors_IsTenantScoped_TenantKey_RecordKind",
                table: "AbpNotificationRetentionCleanupCursors",
                columns: new[] { "IsTenantScoped", "TenantKey", "RecordKind" },
                unique: true);
        }
    }
}
