using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class TurnosAgente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "atendimento_turnos_agente",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: false),
                    MensagemClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    MensagemRespostaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TokensEntrada = table.Column<int>(type: "integer", nullable: false),
                    TokensSaida = table.Column<int>(type: "integer", nullable: false),
                    TokensCacheLido = table.Column<int>(type: "integer", nullable: false),
                    CustoEstimado = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    Ferramentas = table.Column<string>(type: "jsonb", nullable: false),
                    Passos = table.Column<int>(type: "integer", nullable: false),
                    Desfecho = table.Column<int>(type: "integer", nullable: false),
                    DuracaoMs = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_atendimento_turnos_agente", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_turnos_agente_empresa_criado",
                table: "atendimento_turnos_agente",
                columns: new[] { "EmpresaId", "CriadoEm" });

            // #2534: mesmo isolamento por empresa de decisoes_vinculo_telefone (20261114015249).
            migrationBuilder.Sql("""
                ALTER TABLE atendimento_turnos_agente ENABLE ROW LEVEL SECURITY;
                ALTER TABLE atendimento_turnos_agente FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON atendimento_turnos_agente
                    USING (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                    WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                        OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON atendimento_turnos_agente;");
            migrationBuilder.DropTable(
                name: "atendimento_turnos_agente");
        }
    }
}
