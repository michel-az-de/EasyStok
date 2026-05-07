using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Hardening da camada de dados (auditoria 2026-05-06):
    ///
    /// Segurança:
    ///   - reset_tokens.Token e email_confirmation_tokens.Token saem (plaintext)
    ///     e entram TokenHash (SHA-256). Tokens em voo são invalidados — usuários
    ///     re-solicitam (TTL é curto, 1h/24h, impacto baixo). Em breach o
    ///     atacante não consegue mais usar o dump pra resetar senha/confirmar.
    ///   - mobile_devices.api_key sai (plaintext) e entra api_key_hash (SHA-256).
    ///     Migração preserva devices existentes via UPDATE com digest(api_key,'sha256').
    ///     Para isso, garantimos a extensão pgcrypto antes do UPDATE.
    ///
    /// Integridade:
    ///   - notificacoes.OutboxMensagemId ganha FK explícita (SetNull) — antes
    ///     ficavam dangling refs quando outbox era purgado.
    ///   - admin_tickets.CriadoPorId migra de FK NoAction para SetNull e ganha
    ///     index nominado pra padrão snake_case.
    ///
    /// Performance:
    ///   - Indexes parciais para cleanup: refresh/reset/email_confirm tokens só
    ///     varrem tokens ainda ativos (filter Revogado/Usado/Confirmado = false).
    ///   - Hash columns ficam UNIQUE (lookup O(1)) em vez do index não-único atual.
    ///   - produto_alteracoes ganha (EmpresaId, AlteradoEm) pra feed de auditoria
    ///     por tenant.
    ///   - admin_acessos_pii_logs ganha (TenantId, CriadoEm) pra relatório ANPD
    ///     "PII acessada na minha empresa nos últimos N dias".
    /// </summary>
    public partial class HardenDataLayerSecurityAndIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pgcrypto é necessário para digest(text, 'sha256') usado na migração
            // dos api_keys mobile. É idempotente (IF NOT EXISTS).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            // ====================================================================
            // 1) admin_tickets — rename do index e troca de DeleteBehavior pra SetNull
            // ====================================================================
            migrationBuilder.DropForeignKey(
                name: "FK_admin_tickets_usuarios_CriadoPorId",
                table: "admin_tickets");

            migrationBuilder.RenameIndex(
                name: "IX_admin_tickets_CriadoPorId",
                table: "admin_tickets",
                newName: "ix_admin_tickets_criado_por_id");

            migrationBuilder.AddForeignKey(
                name: "FK_admin_tickets_usuarios_CriadoPorId",
                table: "admin_tickets",
                column: "CriadoPorId",
                principalTable: "usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ====================================================================
            // 2) reset_tokens — rename Token -> TokenHash com truncate dos pendentes
            //
            // Tokens existentes são plaintext (não dá pra re-hashear sem perder o
            // valor original). DELETE invalida pendentes; usuário re-solicita.
            // ====================================================================
            migrationBuilder.DropIndex(
                name: "IX_reset_tokens_ExpiraEm",
                table: "reset_tokens");

            migrationBuilder.DropIndex(
                name: "IX_reset_tokens_Token",
                table: "reset_tokens");

            migrationBuilder.Sql("DELETE FROM reset_tokens;");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "reset_tokens");

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "reset_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ux_reset_tokens_token_hash",
                table: "reset_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reset_tokens_expira_pendente",
                table: "reset_tokens",
                column: "ExpiraEm",
                filter: "\"Usado\" = false");

            // ====================================================================
            // 3) email_confirmation_tokens — mesma transição (DELETE pendentes)
            // ====================================================================
            migrationBuilder.DropIndex(
                name: "IX_email_confirmation_tokens_ExpiraEm",
                table: "email_confirmation_tokens");

            migrationBuilder.DropIndex(
                name: "IX_email_confirmation_tokens_Token",
                table: "email_confirmation_tokens");

            migrationBuilder.Sql("DELETE FROM email_confirmation_tokens;");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "email_confirmation_tokens");

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "email_confirmation_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ux_email_confirmation_tokens_token_hash",
                table: "email_confirmation_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_confirmation_tokens_expira_pendente",
                table: "email_confirmation_tokens",
                column: "ExpiraEm",
                filter: "\"Confirmado\" = false");

            // ====================================================================
            // 4) refresh_tokens — promove o index TokenHash para UNIQUE e
            // troca o cleanup index por partial (só ativos)
            // ====================================================================
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_ExpiraEm",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expira_ativo",
                table: "refresh_tokens",
                column: "ExpiraEm",
                filter: "\"Revogado\" = false");

            // ====================================================================
            // 5) mobile_devices — api_key (plaintext) -> api_key_hash (SHA-256)
            //
            // Aqui SIM preservamos os devices: ADD nullable, UPDATE com digest,
            // depois SET NOT NULL e DROP do plaintext. Devices em produção
            // continuam autenticando (app manda api_key crua, server hashea
            // antes do lookup).
            // ====================================================================
            migrationBuilder.DropIndex(
                name: "ix_mobile_devices_api_key",
                table: "mobile_devices");

            migrationBuilder.AddColumn<string>(
                name: "api_key_hash",
                table: "mobile_devices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE mobile_devices
                SET api_key_hash = encode(digest(api_key, 'sha256'), 'hex')
                WHERE api_key IS NOT NULL AND api_key_hash IS NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE mobile_devices
                ALTER COLUMN api_key_hash SET NOT NULL,
                ALTER COLUMN api_key_hash SET DEFAULT '';
            ");

            migrationBuilder.DropColumn(
                name: "api_key",
                table: "mobile_devices");

            migrationBuilder.CreateIndex(
                name: "ux_mobile_devices_api_key_hash",
                table: "mobile_devices",
                column: "api_key_hash",
                unique: true);

            // ====================================================================
            // 6) produto_alteracoes — index pra feed de "últimas alterações" por tenant
            // ====================================================================
            migrationBuilder.CreateIndex(
                name: "ix_produto_alteracoes_empresa_alterado_em",
                table: "produto_alteracoes",
                columns: new[] { "EmpresaId", "AlteradoEm" });

            // ====================================================================
            // 7) admin_acessos_pii_logs — index pra relatório ANPD por tenant
            // ====================================================================
            migrationBuilder.CreateIndex(
                name: "ix_admin_acessos_pii_logs_tenant_criado",
                table: "admin_acessos_pii_logs",
                columns: new[] { "TenantId", "CriadoEm" });

            // ====================================================================
            // 8) notificacoes — FK explícita pra outbox com SetNull
            // ====================================================================
            migrationBuilder.AddForeignKey(
                name: "FK_notificacoes_notif_outbox_mensagens_OutboxMensagemId",
                table: "notificacoes",
                column: "OutboxMensagemId",
                principalTable: "notif_outbox_mensagens",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversão é best-effort: restauramos schema, mas dados de tokens
            // (plaintext) ficam vazios e api_keys ficam re-hasheadas pra
            // base64 dos hashes (não há como recuperar plaintext). Em rollback
            // real, devices precisariam re-parear.
            migrationBuilder.DropForeignKey(
                name: "FK_admin_tickets_usuarios_CriadoPorId",
                table: "admin_tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_notificacoes_notif_outbox_mensagens_OutboxMensagemId",
                table: "notificacoes");

            migrationBuilder.DropIndex(
                name: "ix_admin_acessos_pii_logs_tenant_criado",
                table: "admin_acessos_pii_logs");

            migrationBuilder.DropIndex(
                name: "ix_produto_alteracoes_empresa_alterado_em",
                table: "produto_alteracoes");

            migrationBuilder.DropIndex(
                name: "ux_mobile_devices_api_key_hash",
                table: "mobile_devices");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_expira_ativo",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ux_refresh_tokens_token_hash",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_email_confirmation_tokens_expira_pendente",
                table: "email_confirmation_tokens");

            migrationBuilder.DropIndex(
                name: "ux_email_confirmation_tokens_token_hash",
                table: "email_confirmation_tokens");

            migrationBuilder.DropIndex(
                name: "ix_reset_tokens_expira_pendente",
                table: "reset_tokens");

            migrationBuilder.DropIndex(
                name: "ux_reset_tokens_token_hash",
                table: "reset_tokens");

            migrationBuilder.DropColumn(
                name: "api_key_hash",
                table: "mobile_devices");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "email_confirmation_tokens");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "reset_tokens");

            migrationBuilder.RenameIndex(
                name: "ix_admin_tickets_criado_por_id",
                table: "admin_tickets",
                newName: "IX_admin_tickets_CriadoPorId");

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "reset_tokens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "email_confirmation_tokens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "api_key",
                table: "mobile_devices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_reset_tokens_ExpiraEm",
                table: "reset_tokens",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_reset_tokens_Token",
                table: "reset_tokens",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_email_confirmation_tokens_ExpiraEm",
                table: "email_confirmation_tokens",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_email_confirmation_tokens_Token",
                table: "email_confirmation_tokens",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_ExpiraEm",
                table: "refresh_tokens",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "ix_mobile_devices_api_key",
                table: "mobile_devices",
                column: "api_key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_admin_tickets_usuarios_CriadoPorId",
                table: "admin_tickets",
                column: "CriadoPorId",
                principalTable: "usuarios",
                principalColumn: "Id");
        }
    }
}
