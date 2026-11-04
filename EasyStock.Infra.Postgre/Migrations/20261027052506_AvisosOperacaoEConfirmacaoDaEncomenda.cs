using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AvisosOperacaoEConfirmacaoDaEncomenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "confirmado_pelo_cliente_em",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avisos_operacao",
                table: "configuracoes_atendimento",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confirmado_pelo_cliente_em",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "avisos_operacao",
                table: "configuracoes_atendimento");
        }
    }
}
