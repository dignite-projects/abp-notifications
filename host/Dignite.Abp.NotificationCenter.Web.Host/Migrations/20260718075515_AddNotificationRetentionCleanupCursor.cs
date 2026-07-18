using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Abp.NotificationCenter.Web.Host.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRetentionCleanupCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpNotificationRetentionCleanupCursors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantKey = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsTenantScoped = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecordKind = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCreationTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpNotificationRetentionCleanupCursors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpNotificationRetentionCleanupCursors_IsTenantScoped_TenantKey_RecordKind",
                table: "AbpNotificationRetentionCleanupCursors",
                columns: new[] { "IsTenantScoped", "TenantKey", "RecordKind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpNotificationRetentionCleanupCursors");
        }
    }
}
