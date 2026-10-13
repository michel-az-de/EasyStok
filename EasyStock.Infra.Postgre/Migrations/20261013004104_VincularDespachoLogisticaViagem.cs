using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class VincularDespachoLogisticaViagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ViagemId",
                table: "logistica_despachos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_logistica_despachos_ViagemId",
                table: "logistica_despachos",
                column: "ViagemId");

            migrationBuilder.AddForeignKey(
                name: "FK_logistica_despachos_viagens_ViagemId",
                table: "logistica_despachos",
                column: "ViagemId",
                principalTable: "viagens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_logistica_despachos_viagens_ViagemId",
                table: "logistica_despachos");

            migrationBuilder.DropIndex(
                name: "IX_logistica_despachos_ViagemId",
                table: "logistica_despachos");

            migrationBuilder.DropColumn(
                name: "ViagemId",
                table: "logistica_despachos");
        }
    }
}
