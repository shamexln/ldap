using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImprivataProxy.Sources.Local.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Event = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    ServerState = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PendingModality = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.ServerState);
                });

            migrationBuilder.CreateTable(
                name: "TicketBlacklist",
                columns: table => new
                {
                    Jti = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketBlacklist", x => x.Jti);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AdObjectGuid = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AdDistinguishedName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PwdHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    PwdHashUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PinHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    PinFailCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PinLockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PwdFailCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PwdLockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserCards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CardUidHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CardUidLast4 = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Revoked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCards_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Event",
                table: "AuditLog",
                column: "Event");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Timestamp",
                table: "AuditLog",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_ExpiresAt",
                table: "AuthSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_TicketBlacklist_ExpiresAt",
                table: "TicketBlacklist",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserCards_CardUidHash",
                table: "UserCards",
                column: "CardUidHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCards_UserId",
                table: "UserCards",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_AdObjectGuid",
                table: "Users",
                column: "AdObjectGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username_Domain",
                table: "Users",
                columns: new[] { "Username", "Domain" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "AuthSessions");

            migrationBuilder.DropTable(
                name: "TicketBlacklist");

            migrationBuilder.DropTable(
                name: "UserCards");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
