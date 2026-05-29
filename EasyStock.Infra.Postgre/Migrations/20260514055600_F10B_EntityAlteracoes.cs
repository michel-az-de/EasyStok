using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class F10B_EntityAlteracoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_alteracoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoEntidade = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    EntidadeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Acao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ValorAntigo = table.Column<string>(type: "text", nullable: true),
                    ValorNovo = table.Column<string>(type: "text", nullable: true),
                    AlteradoPorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlteradoPorNome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteracoesJson = table.Column<string>(type: "text", nullable: true),
                    PiiCriptografado = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_alteracoes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_alteracoes_lookup",
                table: "entity_alteracoes",
                columns: new[] { "EmpresaId", "TipoEntidade", "EntidadeId", "AlteradoEm" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_entity_alteracoes_retention",
                table: "entity_alteracoes",
                columns: new[] { "EmpresaId", "AlteradoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_alteracoes");
        }
    }
}
