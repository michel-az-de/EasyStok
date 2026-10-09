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
            throw new NotSupportedException("Restaure o arquivo de permissões e revise os perfis explícitos antes de reverter.");
        }
    }
}
