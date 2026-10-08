using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddMarcasExclusaoDescarteLoteMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "mobile_batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deleted_by",
                table: "mobile_batches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discard_reason",
                table: "mobile_batches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "discarded_at",
                table: "mobile_batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discarded_by",
                table: "mobile_batches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "mobile_batches");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                table: "mobile_batches");

            migrationBuilder.DropColumn(
                name: "discard_reason",
                table: "mobile_batches");

            migrationBuilder.DropColumn(
                name: "discarded_at",
                table: "mobile_batches");

            migrationBuilder.DropColumn(
                name: "discarded_by",
                table: "mobile_batches");
        }
    }
}
