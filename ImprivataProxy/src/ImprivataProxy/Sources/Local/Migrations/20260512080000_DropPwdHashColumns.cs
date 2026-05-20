using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImprivataProxy.Sources.Local.Migrations
{
    /// <inheritdoc />
    public partial class DropPwdHashColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PwdHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PwdHashUpdatedAt",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PwdHash",
                table: "Users",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PwdHashUpdatedAt",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }
    }
}
