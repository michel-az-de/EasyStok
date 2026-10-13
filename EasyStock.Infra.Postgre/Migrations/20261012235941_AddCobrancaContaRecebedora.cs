using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddCobrancaContaRecebedora : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AguardandoCredencialDesde",
                table: "cobrancas_pedido",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContaRecebedoraId",
                table: "cobrancas_pedido",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrigemCredencial",
                table: "cobrancas_pedido",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // I3.2: cobrança online criada antes desta migration nasceu da credencial global do .env.
            // A tabela tem RLS forçado; set_config com is_local = true vale só nesta transação.
            migrationBuilder.Sql("""
SELECT set_config('app.bypass_rls', 'true', true);
UPDATE cobrancas_pedido SET "OrigemCredencial" = 'global' WHERE "Provedor" = 'mercadopago' AND "OrigemCredencial" IS NULL;
SELECT set_config('app.bypass_rls', '', true);
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AguardandoCredencialDesde",
                table: "cobrancas_pedido");

            migrationBuilder.DropColumn(
                name: "ContaRecebedoraId",
                table: "cobrancas_pedido");

            migrationBuilder.DropColumn(
                name: "OrigemCredencial",
                table: "cobrancas_pedido");
        }
    }
}
