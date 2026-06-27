using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Seed das duas FreteZonas iniciais do storefront casa-da-baba.
    /// Storefront fica em Butanta (CEP origem 05382-070, Felipe 2026-06-25).
    ///
    /// Idempotente via ON CONFLICT DO NOTHING — re-roda sem efeito colateral.
    /// Sem Down() — seed n.ao desfaz (politica do repo).
    ///
    /// Guarda WHERE EXISTS(storefront 3e4842d9): o storefront e app-seeded, nao criado
    /// por migration. Em DB limpo (migrate-from-scratch: CI, novo ambiente, restore-DR)
    /// ele ainda nao existe, entao o seed simplesmente NAO insere — sem violar a FK
    /// frete_zona->storefront. Em prod, onde o storefront ja existe, insere normal.
    /// (Bug exposto pela suite EasyStock.Inventario.IntegrationTests; ver issue #704.)
    ///
    /// Frete por raio (ADR-0017) fica pra issue separada: precisa subir
    /// container Nominatim self-host na VM (ADR-0023). Enquanto isso, zona
    /// faz o trabalho.
    ///
    /// Zona 1 (core Butanta, ordem 0): CEPs 05380000-05389999, R$10, 30min.
    /// Zona 2 (entorno 053xx, ordem 10): CEPs 05300000-05399999, R$18, 45min.
    /// Use case itera por Ordem ASC: 0538* casa Zona 1; resto de 053* cai
    /// na Zona 2.
    /// </summary>
    public partial class SeedCasaDaBabaFreteZonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO frete_zona (
    ""Id"", ""StorefrontId"", ""Ordem"", ""Label"", ""Valor"",
    ""TempoEstimadoMinutos"", ""Ativa"", ""TipoCobertura"",
    ""CepInicio"", ""CepFim"", ""BairrosJson"",
    ""CriadoEm"", ""AlteradoEm""
)
SELECT
    '8c5f5e10-0000-4000-8000-c0a5ada00001',
    '3e4842d9-2994-47cb-b86c-870fe248ff4d',
    0,
    'Butantã (proximidade)',
    10.00,
    30,
    TRUE,
    'cep_range',
    '05380000',
    '05389999',
    NULL,
    now() AT TIME ZONE 'UTC',
    now() AT TIME ZONE 'UTC'
WHERE EXISTS (SELECT 1 FROM storefront WHERE ""Id"" = '3e4842d9-2994-47cb-b86c-870fe248ff4d')
ON CONFLICT (""Id"") DO NOTHING;

INSERT INTO frete_zona (
    ""Id"", ""StorefrontId"", ""Ordem"", ""Label"", ""Valor"",
    ""TempoEstimadoMinutos"", ""Ativa"", ""TipoCobertura"",
    ""CepInicio"", ""CepFim"", ""BairrosJson"",
    ""CriadoEm"", ""AlteradoEm""
)
SELECT
    '8c5f5e10-0000-4000-8000-c0a5ada00002',
    '3e4842d9-2994-47cb-b86c-870fe248ff4d',
    10,
    'Entorno Butantã/Vila Sônia/Morumbi',
    18.00,
    45,
    TRUE,
    'cep_range',
    '05300000',
    '05399999',
    NULL,
    now() AT TIME ZONE 'UTC',
    now() AT TIME ZONE 'UTC'
WHERE EXISTS (SELECT 1 FROM storefront WHERE ""Id"" = '3e4842d9-2994-47cb-b86c-870fe248ff4d')
ON CONFLICT (""Id"") DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Seed nao desfaz — politica do repo.
        }
    }
}
