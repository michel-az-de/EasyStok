using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// S41: quem do console enviou a mensagem. Coluna nula (mensagens antigas e as do agente e do
    /// sistema ficam sem usuário); a tabela já tem RLS.
    /// </summary>
    public partial class AddEnviadaPorUsuarioIdMensagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EnviadaPorUsuarioId",
                table: "atendimento_mensagens",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnviadaPorUsuarioId",
                table: "atendimento_mensagens");
        }
    }
}