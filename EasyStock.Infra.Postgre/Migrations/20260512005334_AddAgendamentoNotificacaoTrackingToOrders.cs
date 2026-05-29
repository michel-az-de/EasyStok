using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddAgendamentoNotificacaoTrackingToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "agendamento_notificado_10min_em",
                table: "mobile_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "agendamento_notificado_1h_em",
                table: "mobile_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "agendamento_notificado_dia_em",
                table: "mobile_orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agendamento_notificado_10min_em",
                table: "mobile_orders");

            migrationBuilder.DropColumn(
                name: "agendamento_notificado_1h_em",
                table: "mobile_orders");

            migrationBuilder.DropColumn(
                name: "agendamento_notificado_dia_em",
                table: "mobile_orders");
        }
    }
}
