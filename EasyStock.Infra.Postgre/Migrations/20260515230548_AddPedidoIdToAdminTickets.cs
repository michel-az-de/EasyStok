using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddPedidoIdToAdminTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PedidoId",
                table: "admin_tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_tickets_pedido_id",
                table: "admin_tickets",
                column: "PedidoId",
                filter: "\"PedidoId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_admin_tickets_pedidos_PedidoId",
                table: "admin_tickets",
                column: "PedidoId",
                principalTable: "pedidos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_admin_tickets_pedidos_PedidoId",
                table: "admin_tickets");

            migrationBuilder.DropIndex(
                name: "ix_admin_tickets_pedido_id",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "PedidoId",
                table: "admin_tickets");
        }
    }
}
