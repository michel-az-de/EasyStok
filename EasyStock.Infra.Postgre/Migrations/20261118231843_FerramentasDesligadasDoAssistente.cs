using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>#2853: ferramentas do Assistente desligadas pela loja. Anulável: nulo = todas ligadas, como antes.</summary>
    public partial class FerramentasDesligadasDoAssistente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FerramentasDesligadasJson",
                table: "configuracoes_atendimento",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FerramentasDesligadasJson",
                table: "configuracoes_atendimento");
        }
    }
}
