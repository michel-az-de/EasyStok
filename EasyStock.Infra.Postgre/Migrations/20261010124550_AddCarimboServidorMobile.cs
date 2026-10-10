using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// #1520 (ADR-0060) — carimbo do servidor nas tabelas que o pull do PWA devolve. Aditiva:
    /// coluna NOT NULL com default now(), backfill pelo valor que o pull usava ate aqui e indice
    /// (empresa, carimbo) para o filtro.
    /// </summary>
    public partial class AddCarimboServidorMobile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "server_updated_at",
                table: "mobile_products",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTime>(
                name: "server_updated_at",
                table: "mobile_orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTime>(
                name: "server_updated_at",
                table: "mobile_clients",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTime>(
                name: "server_updated_at",
                table: "mobile_cash_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTime>(
                name: "server_updated_at",
                table: "mobile_batches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            // Backfill: as linhas que ja existem ficam com o valor que o pull filtrava (updated_at
            // ou created_at), para nenhum aparelho receber tudo de novo. Hora do aparelho no
            // futuro (relogio adiantado) e cortada em now(): carimbo do servidor nunca e futuro.
            migrationBuilder.Sql("""
                UPDATE mobile_products     SET server_updated_at = LEAST(updated_at, now());
                UPDATE mobile_clients      SET server_updated_at = LEAST(updated_at, now());
                UPDATE mobile_orders       SET server_updated_at = LEAST(updated_at, now());
                UPDATE mobile_batches      SET server_updated_at = LEAST(created_at, now());
                UPDATE mobile_cash_entries SET server_updated_at = LEAST(created_at, now());
                """);

            migrationBuilder.CreateIndex(
                name: "ix_mobile_products_empresa_carimbo",
                table: "mobile_products",
                columns: new[] { "empresa_id", "server_updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_orders_empresa_carimbo",
                table: "mobile_orders",
                columns: new[] { "empresa_id", "server_updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_clients_empresa_carimbo",
                table: "mobile_clients",
                columns: new[] { "empresa_id", "server_updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_cash_entries_empresa_carimbo",
                table: "mobile_cash_entries",
                columns: new[] { "empresa_id", "server_updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mobile_batches_empresa_carimbo",
                table: "mobile_batches",
                columns: new[] { "empresa_id", "server_updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mobile_products_empresa_carimbo",
                table: "mobile_products");

            migrationBuilder.DropIndex(
                name: "ix_mobile_orders_empresa_carimbo",
                table: "mobile_orders");

            migrationBuilder.DropIndex(
                name: "ix_mobile_clients_empresa_carimbo",
                table: "mobile_clients");

            migrationBuilder.DropIndex(
                name: "ix_mobile_cash_entries_empresa_carimbo",
                table: "mobile_cash_entries");

            migrationBuilder.DropIndex(
                name: "ix_mobile_batches_empresa_carimbo",
                table: "mobile_batches");

            migrationBuilder.DropColumn(
                name: "server_updated_at",
                table: "mobile_products");

            migrationBuilder.DropColumn(
                name: "server_updated_at",
                table: "mobile_orders");

            migrationBuilder.DropColumn(
                name: "server_updated_at",
                table: "mobile_clients");

            migrationBuilder.DropColumn(
                name: "server_updated_at",
                table: "mobile_cash_entries");

            migrationBuilder.DropColumn(
                name: "server_updated_at",
                table: "mobile_batches");
        }
    }
}
