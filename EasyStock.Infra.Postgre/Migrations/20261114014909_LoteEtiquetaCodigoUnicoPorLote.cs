using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class LoteEtiquetaCodigoUnicoPorLote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lote_etiquetas_Codigo",
                table: "lote_etiquetas");

            migrationBuilder.CreateIndex(
                name: "IX_lote_etiquetas_LoteId_Codigo",
                table: "lote_etiquetas",
                columns: new[] { "LoteId", "Codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lote_etiquetas_LoteId_Codigo",
                table: "lote_etiquetas");

            migrationBuilder.CreateIndex(
                name: "IX_lote_etiquetas_Codigo",
                table: "lote_etiquetas",
                column: "Codigo",
                unique: true);
        }
    }
}
