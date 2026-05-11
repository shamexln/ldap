using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImprivataProxy.Sources.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddGivenNameSn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GivenName",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sn",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GivenName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Sn",
                table: "Users");
        }
    }
}
