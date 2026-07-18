using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Abp.NotificationCenter.Web.Host.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryAndPreferenceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationSubscriptions_TenantId_NotificationName_EntityTypeName_EntityId",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationSubscriptions_TenantId_UserId_NotificationName_EntityTypeName_EntityId",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationSubscriptions_UserId_NotificationName",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.AddColumn<string>(
                name: "NotificationNameKey",
                table: "AbpNotificationSubscriptions",
                type: "TEXT",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScopeKey",
                table: "AbpNotificationSubscriptions",
                type: "TEXT",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantKey",
                table: "AbpNotificationSubscriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "AbpNotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ChannelKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Intent = table.Column<int>(type: "INTEGER", nullable: false),
                    DeliveryNotBefore = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreferenceReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 89, nullable: false),
                    NotificationName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    EntityTypeName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Severity = table.Column<byte>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAttemptTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeaseExpirationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastFailureMessage = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NotificationNameKey = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ChannelKey = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    EndMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpNotificationQuietHours", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationSubscriptions_TenantKey_NotificationNameKey_ScopeKey",
                table: "AbpNotificationSubscriptions",
                columns: new[] { "TenantKey", "NotificationNameKey", "ScopeKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationSubscriptions_TenantKey_UserId_NotificationNameKey",
                table: "AbpNotificationSubscriptions",
                columns: new[] { "TenantKey", "UserId", "NotificationNameKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationSubscriptions_TenantKey_UserId_NotificationNameKey_ScopeKey",
                table: "AbpNotificationSubscriptions",
                columns: new[] { "TenantKey", "UserId", "NotificationNameKey", "ScopeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_State_LeaseExpirationTime",
                table: "AbpNotificationDeliveries",
                columns: new[] { "State", "LeaseExpirationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_State_NextAttemptTime",
                table: "AbpNotificationDeliveries",
                columns: new[] { "State", "NextAttemptTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationDeliveries_TenantKey_NotificationId_UserId_ChannelKey",
                table: "AbpNotificationDeliveries",
                columns: new[] { "TenantKey", "NotificationId", "UserId", "ChannelKey" },
                unique: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpNotificationDeliveries");

            migrationBuilder.DropTable(
                name: "AbpNotificationDeliveryPreferences");

            migrationBuilder.DropTable(
                name: "AbpNotificationQuietHours");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationSubscriptions_TenantKey_NotificationNameKey_ScopeKey",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationSubscriptions_TenantKey_UserId_NotificationNameKey",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AbpNotificationSubscriptions_TenantKey_UserId_NotificationNameKey_ScopeKey",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.DropColumn(
                name: "NotificationNameKey",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.DropColumn(
                name: "ScopeKey",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.DropColumn(
                name: "TenantKey",
                table: "AbpNotificationSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationSubscriptions_TenantId_NotificationName_EntityTypeName_EntityId",
                table: "AbpNotificationSubscriptions",
                columns: new[] { "TenantId", "NotificationName", "EntityTypeName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationSubscriptions_TenantId_UserId_NotificationName_EntityTypeName_EntityId",
                table: "AbpNotificationSubscriptions",
                columns: new[] { "TenantId", "UserId", "NotificationName", "EntityTypeName", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationSubscriptions_UserId_NotificationName",
                table: "AbpNotificationSubscriptions",
                columns: new[] { "UserId", "NotificationName" });
        }
    }
}
