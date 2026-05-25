using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// F5 — Agendamento de pedido (MVP). Adiciona ScheduledDeliveryAt em
    /// mobile_orders e AgendadoParaEm em pedidos. Pedidos agendados aparecem
    /// ordenados por essa data no PWA/KDS/grid web e ganham badge visual.
    ///
    /// NOTA: o `dotnet ef migrations add` capturou tambem mudancas de Onboarding
    /// (empresas.NomeFantasia/OnboardingCompleto/Segmento/Telefone/...) e CSAT
    /// (admin_tickets.NotaCsat/AvaliadoEm/...) que estavam no model mas sem
    /// migration. Removidas daqui pra evitar "column already exists" em prod —
    /// elas serao re-detectadas em uma proxima `migrations add` se ainda
    /// estiverem pendentes.
    /// </summary>
    public partial class AddScheduledDeliveryToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AgendadoParaEm",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "scheduled_delivery_at",
                table: "mobile_orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgendadoParaEm",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "scheduled_delivery_at",
                table: "mobile_orders");
        }
    }
}
