using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// S34 (ADR-0051): a identidade da conversa passa a ser (Canal, ContatoIdExterno). Escrita à mão:
    /// o EF gera DropColumn + AddColumn para um rename, o que apagaria o contato de toda conversa
    /// existente. Aqui é RenameColumn + alargamento (e-mail cabe em 256), sem tocar nos dados.
    /// </summary>
    public partial class RenomeiaContatoConversaPorCanal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_atendimento_conversas_empresa_contato_aberta",
                table: "atendimento_conversas");

            migrationBuilder.RenameColumn(
                name: "ContatoWaId",
                table: "atendimento_conversas",
                newName: "ContatoIdExterno");

            migrationBuilder.AlterColumn<string>(
                name: "ContatoIdExterno",
                table: "atendimento_conversas",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "uq_atendimento_conversas_empresa_canal_contato_aberta",
                table: "atendimento_conversas",
                columns: new[] { "EmpresaId", "Canal", "ContatoIdExterno" },
                unique: true,
                filter: "\"Situacao\" <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // O esquema anterior só representa WhatsApp: com conversa de outro canal, voltar
            // colidiria no índice antigo ou truncaria o contato. Recusa em vez de apagar dados.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM atendimento_conversas WHERE "Canal" <> 1) THEN
                        RAISE EXCEPTION 'Existem conversas de canais que nao sao WhatsApp; rollback da S34 recusado para nao perder dados.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "uq_atendimento_conversas_empresa_canal_contato_aberta",
                table: "atendimento_conversas");

            migrationBuilder.AlterColumn<string>(
                name: "ContatoIdExterno",
                table: "atendimento_conversas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.RenameColumn(
                name: "ContatoIdExterno",
                table: "atendimento_conversas",
                newName: "ContatoWaId");

            migrationBuilder.CreateIndex(
                name: "uq_atendimento_conversas_empresa_contato_aberta",
                table: "atendimento_conversas",
                columns: new[] { "EmpresaId", "ContatoWaId" },
                unique: true,
                filter: "\"Situacao\" <> 3");
        }
    }
}
