using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueNfePedidoAtivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FAIL-LOUD (#791): 2 NFC-e ATIVAS para o mesmo pedido = documento fiscal na SEFAZ.
            // NAO deletamos/cancelamos as cegas (cancelamento e ato fiscal regulado). Se prod
            // ja tem duplicatas ativas, a migration ABORTA com a contagem e o Felipe resolve
            // via cancelamento adequado antes de reaplicar. Banco novo nao tem: segue.
            migrationBuilder.Sql("""
                DO $$
                DECLARE dup int;
                BEGIN
                    SELECT COUNT(*) INTO dup FROM (
                        SELECT 1 FROM nfe_documentos
                        WHERE "Status" NOT IN ('Rejeitada', 'Inutilizada')
                        GROUP BY "EmpresaId", "PedidoId"
                        HAVING COUNT(*) > 1
                    ) d;
                    IF dup > 0 THEN
                        RAISE EXCEPTION 'C3/#791: % pedido(s) com 2+ NFC-e ATIVAS. Resolver via cancelamento fiscal adequado ANTES de aplicar (nao deletar linha).', dup;
                    END IF;
                END $$;
                """);

            // Idempotente (ADR-0024 §3).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ux_nfe_pedido_ativo
                  ON nfe_documentos ("EmpresaId", "PedidoId")
                  WHERE "Status" NOT IN ('Rejeitada', 'Inutilizada');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_nfe_pedido_ativo;");
        }
    }
}
