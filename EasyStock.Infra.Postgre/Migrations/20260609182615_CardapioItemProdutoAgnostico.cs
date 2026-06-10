using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class CardapioItemProdutoAgnostico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_cardapio_item_storefront_produto",
                table: "cardapio_item");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProdutoId",
                table: "cardapio_item",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "CategoriaTexto",
                table: "cardapio_item",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomePublico",
                table: "cardapio_item",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_cardapio_item_storefront_produto",
                table: "cardapio_item",
                columns: new[] { "StorefrontId", "ProdutoId" },
                unique: true,
                filter: "\"ProdutoId\" IS NOT NULL");

            // CHECK: garante invariante de domínio no banco ("ProdutoId" OR "NomePublico" NOT NULL).
            // Impede estado podre via import SQL, seeds ou bugs em outros use cases.
            // Colunas em PascalCase citado (convenção do schema). DROP+ADD = idempotente
            // (Postgres nao suporta ADD CONSTRAINT IF NOT EXISTS).
            migrationBuilder.Sql(@"
                ALTER TABLE cardapio_item
                DROP CONSTRAINT IF EXISTS chk_cardapio_item_nome_ou_produto;
                ALTER TABLE cardapio_item
                ADD CONSTRAINT chk_cardapio_item_nome_ou_produto
                CHECK (""ProdutoId"" IS NOT NULL OR ""NomePublico"" IS NOT NULL);");

            // Índice único para itens avulsos (produto_id IS NULL).
            // Previne duplicatas por nome no mesmo storefront.
            // CONCURRENTLY: não bloqueia leituras/escritas durante criação.
            // suppressTransaction: true obrigatório — CONCURRENTLY não pode rodar em transação.
            // IF NOT EXISTS: idempotente (safe re-run).
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS uq_cardapio_item_storefront_nome_avulso
                  ON cardapio_item(""StorefrontId"", LOWER(""NomePublico""))
                  WHERE ""ProdutoId"" IS NULL AND ""NomePublico"" IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX CONCURRENTLY IF EXISTS uq_cardapio_item_storefront_nome_avulso;",
                suppressTransaction: true);

            migrationBuilder.Sql(@"
                ALTER TABLE cardapio_item
                DROP CONSTRAINT IF EXISTS chk_cardapio_item_nome_ou_produto;");

            migrationBuilder.DropIndex(
                name: "uq_cardapio_item_storefront_produto",
                table: "cardapio_item");

            migrationBuilder.DropColumn(
                name: "CategoriaTexto",
                table: "cardapio_item");

            migrationBuilder.DropColumn(
                name: "NomePublico",
                table: "cardapio_item");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProdutoId",
                table: "cardapio_item",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_cardapio_item_storefront_produto",
                table: "cardapio_item",
                columns: new[] { "StorefrontId", "ProdutoId" },
                unique: true);
        }
    }
}
