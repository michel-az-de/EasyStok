using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarEstornoMovimentacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EstornadaEm",
                table: "movimentacoes_estoque",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MovimentacaoEstornadaId",
                table: "movimentacoes_estoque",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_movimentacoes_estoque_MovimentacaoEstornadaId",
                table: "movimentacoes_estoque",
                column: "MovimentacaoEstornadaId",
                filter: "\"MovimentacaoEstornadaId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_movimentacoes_estoque_movimentacoes_estoque_MovimentacaoEst~",
                table: "movimentacoes_estoque",
                column: "MovimentacaoEstornadaId",
                principalTable: "movimentacoes_estoque",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_movimentacoes_estoque_movimentacoes_estoque_MovimentacaoEst~",
                table: "movimentacoes_estoque");

            migrationBuilder.DropIndex(
                name: "IX_movimentacoes_estoque_MovimentacaoEstornadaId",
                table: "movimentacoes_estoque");

            migrationBuilder.DropColumn(
                name: "EstornadaEm",
                table: "movimentacoes_estoque");

            migrationBuilder.DropColumn(
                name: "MovimentacaoEstornadaId",
                table: "movimentacoes_estoque");
        }
    }
}
