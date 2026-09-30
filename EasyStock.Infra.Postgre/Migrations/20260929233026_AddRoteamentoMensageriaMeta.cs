using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddRoteamentoMensageriaMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FacebookPageId",
                table: "empresas",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramAccountId",
                table: "empresas",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_empresas_FacebookPageId",
                table: "empresas",
                column: "FacebookPageId",
                unique: true,
                filter: "\"FacebookPageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_empresas_InstagramAccountId",
                table: "empresas",
                column: "InstagramAccountId",
                unique: true,
                filter: "\"InstagramAccountId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_empresas_FacebookPageId",
                table: "empresas");

            migrationBuilder.DropIndex(
                name: "IX_empresas_InstagramAccountId",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "FacebookPageId",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "InstagramAccountId",
                table: "empresas");
        }
    }
}
