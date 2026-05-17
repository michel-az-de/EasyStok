using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddNfceTaxFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PR-D: Campos de tributos por linha em NfeItem (F-1 / §25.3 do plano).
            // Nullable — NFC-e emitidas antes desta migration ficam com NULL (exibir como "Não rastreado").
            // Populados prospectivamente pelo parser/emissor a partir da Fase F2.

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_itens ADD COLUMN IF NOT EXISTS ""BaseIcms"" numeric(14,2) NULL;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_itens ADD COLUMN IF NOT EXISTS ""ValorIcms"" numeric(14,2) NULL;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_itens ADD COLUMN IF NOT EXISTS ""Pis"" numeric(14,2) NULL;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_itens ADD COLUMN IF NOT EXISTS ""Cofins"" numeric(14,2) NULL;",
                suppressTransaction: true);

            // PR-D: Protocolo SEFAZ do evento de cancelamento/inutilização em NfeEvento (F-3).
            // Distinto do ProtocoloAutorizacao do NfeDocumento.
            // NULL para eventos que não geram protocolo próprio (criado, enviado, erro_transiente).

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_eventos ADD COLUMN IF NOT EXISTS ""ProtocoloEvento"" varchar(50) NULL;",
                suppressTransaction: true);

            // PR-D: Índices de performance para relatórios fiscais (F-4, F-5 do plano §25.3).
            // CREATE INDEX CONCURRENTLY não pode rodar dentro de transação — suppressTransaction obrigatório.
            // IF NOT EXISTS garante idempotência (migration re-run após falha parcial não quebra).

            // F-4: Livro de Saídas por período — filtro por EmpresaId + DataAutorizacao.
            // WHERE parcial cobre apenas NFC-e Autorizadas (status armazenado como string via HasConversion<string>()).
            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_nfe_docs_data_autorizacao
                  ON nfe_documentos (""EmpresaId"", ""DataAutorizacao"")
                  WHERE ""Status"" = 'Autorizada';",
                suppressTransaction: true);

            // F-5a: Totalizadores por NCM — agregação por documento + NCM.
            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_nfe_itens_ncm
                  ON nfe_itens (""NfeDocumentoId"", ""NcmSnapshot"")
                  WHERE ""NcmSnapshot"" IS NOT NULL;",
                suppressTransaction: true);

            // F-5b: Totalizadores por CFOP — agregação por documento + CFOP.
            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_nfe_itens_cfop
                  ON nfe_itens (""NfeDocumentoId"", ""CfopSnapshot"")
                  WHERE ""CfopSnapshot"" IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remover índices antes das colunas (boa prática; Postgres remove automaticamente
            // índices ligados à coluna, mas DROP explícito evita warnings).

            migrationBuilder.Sql(
                @"DROP INDEX CONCURRENTLY IF EXISTS ix_nfe_itens_cfop;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"DROP INDEX CONCURRENTLY IF EXISTS ix_nfe_itens_ncm;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"DROP INDEX CONCURRENTLY IF EXISTS ix_nfe_docs_data_autorizacao;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_eventos DROP COLUMN IF EXISTS ""ProtocoloEvento"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_itens DROP COLUMN IF EXISTS ""Cofins"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_itens DROP COLUMN IF EXISTS ""Pis"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_itens DROP COLUMN IF EXISTS ""ValorIcms"";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                @"ALTER TABLE nfe_itens DROP COLUMN IF EXISTS ""BaseIcms"";",
                suppressTransaction: true);
        }
    }
}
