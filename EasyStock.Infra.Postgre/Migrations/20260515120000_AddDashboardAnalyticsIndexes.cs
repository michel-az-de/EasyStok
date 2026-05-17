using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_movimentos_caixa_EmpresaId_DataMovimento",
                table: "movimentos_caixa",
                columns: new[] { "EmpresaId", "DataMovimento" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_movimentos_caixa_EmpresaId_DataMovimento",
                table: "movimentos_caixa");
        }
    }
}
