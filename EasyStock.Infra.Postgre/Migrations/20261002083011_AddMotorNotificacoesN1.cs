using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddMotorNotificacoesN1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderMensagemId",
                table: "notif_outbox_mensagens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notif_outbox_provider_mensagem_id",
                table: "notif_outbox_mensagens",
                column: "ProviderMensagemId",
                filter: "\"ProviderMensagemId\" IS NOT NULL");

            // N1, catálogo global legível (ADR-0010, camada 2): a tenant_isolation só admite a linha do tenant da sessão
            // ou o bypass, então a rotina, o template, a configuração de canal e o kill switch globais (EmpresaId
            // nulo) ficavam invisíveis a quem roda no escopo de uma empresa. Policies permissivas somam: esta só
            // concede SELECT; UPDATE, DELETE e INSERT seguem sob a tenant_isolation (a empresa não altera nem cria
            // linha global). Idempotente (DROP IF EXISTS antes do CREATE).
            migrationBuilder.Sql("""
DO $catalogo$
DECLARE
    tabela TEXT;
BEGIN
    FOREACH tabela IN ARRAY ARRAY['notif_rotinas', 'notif_templates', 'notif_configuracoes_canal', 'notif_bloqueios']
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS catalogo_global_leitura ON %I', tabela);
        EXECUTE format('CREATE POLICY catalogo_global_leitura ON %I FOR SELECT USING ("EmpresaId" IS NULL)', tabela);
    END LOOP;
END
$catalogo$;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP POLICY IF EXISTS catalogo_global_leitura ON notif_rotinas;
DROP POLICY IF EXISTS catalogo_global_leitura ON notif_templates;
DROP POLICY IF EXISTS catalogo_global_leitura ON notif_configuracoes_canal;
DROP POLICY IF EXISTS catalogo_global_leitura ON notif_bloqueios;
""");

            migrationBuilder.DropIndex(
                name: "ix_notif_outbox_provider_mensagem_id",
                table: "notif_outbox_mensagens");

            migrationBuilder.DropColumn(
                name: "ProviderMensagemId",
                table: "notif_outbox_mensagens");
        }
    }
}
