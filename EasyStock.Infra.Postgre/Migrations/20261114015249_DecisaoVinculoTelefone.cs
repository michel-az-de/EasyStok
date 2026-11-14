using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class DecisaoVinculoTelefone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "decisoes_vinculo_telefone",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelefoneHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClienteRecusadoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClienteEscolhidoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversaOrigemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteracaoVinculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevogadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevogadaPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decisoes_vinculo_telefone", x => x.Id);
                    table.ForeignKey(
                        name: "FK_decisoes_vinculo_telefone_alteracoes_vinculo_conversa_Alter~",
                        column: x => x.AlteracaoVinculoId,
                        principalTable: "alteracoes_vinculo_conversa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_decisoes_vinculo_telefone_atendimento_conversas_ConversaOri~",
                        column: x => x.ConversaOrigemId,
                        principalTable: "atendimento_conversas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_decisoes_vinculo_telefone_AlteracaoVinculoId",
                table: "decisoes_vinculo_telefone",
                column: "AlteracaoVinculoId");

            migrationBuilder.CreateIndex(
                name: "IX_decisoes_vinculo_telefone_ConversaOrigemId",
                table: "decisoes_vinculo_telefone",
                column: "ConversaOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_decisoes_vinculo_telefone_EmpresaId_TelefoneHash",
                table: "decisoes_vinculo_telefone",
                columns: new[] { "EmpresaId", "TelefoneHash" },
                unique: true,
                filter: "\"RevogadaEm\" IS NULL");

            // #2492: mesmo isolamento de alteracoes_vinculo_conversa (20261112191242).
            migrationBuilder.Sql("""
                ALTER TABLE decisoes_vinculo_telefone ENABLE ROW LEVEL SECURITY;
                ALTER TABLE decisoes_vinculo_telefone FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON decisoes_vinculo_telefone
                    USING (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                    WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON decisoes_vinculo_telefone;");
            migrationBuilder.DropTable(
                name: "decisoes_vinculo_telefone");
        }
    }
}
