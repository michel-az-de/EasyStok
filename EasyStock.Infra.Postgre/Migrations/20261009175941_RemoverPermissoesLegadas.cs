using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class RemoverPermissoesLegadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PermissoesExplicitas",
                table: "perfis",
                type: "boolean",
                nullable: false,
                defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "PermissoesLegadas", table: "perfis", type: "jsonb", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem restauração prévia, apagar o marcador pode ampliar acesso e apagar o arquivo perde o histórico.
            // #1504: a trava vale só quando há o que proteger. O RAISE aborta a transação da migration, que
            // continua aplicada; sem perfil explícito nem arquivo, as colunas saem como em qualquer Down aditivo.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM perfis WHERE "PermissoesExplicitas" OR "PermissoesLegadas" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Restaure o arquivo de permissões e revise os perfis explícitos antes de reverter.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(name: "PermissoesLegadas", table: "perfis");
            migrationBuilder.DropColumn(name: "PermissoesExplicitas", table: "perfis");
        }
    }
}
