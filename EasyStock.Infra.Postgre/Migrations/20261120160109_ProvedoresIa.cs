using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class ProvedoresIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ModeloIaId",
                table: "configuracoes_atendimento",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "provedor_ia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    base_url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    autenticacao = table.Column<int>(type: "integer", nullable: false),
                    ligado = table.Column<bool>(type: "boolean", nullable: false),
                    orcamento_mensal_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    credencial_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    alterado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provedor_ia", x => x.id);
                    table.UniqueConstraint("ak_provedor_ia_empresa_id", x => new { x.empresa_id, x.id });
                    table.ForeignKey(
                        name: "fk_provedor_ia_credencial",
                        column: x => x.credencial_id,
                        principalTable: "credencial_integracao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "modelo_ia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_externo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rotulo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ligado = table.Column<bool>(type: "boolean", nullable: false),
                    padrao = table.Column<bool>(type: "boolean", nullable: false),
                    preco_entrada = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    preco_saida = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    preco_cache_lido = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    fator_fora_pico = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false, defaultValue: 1m),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    alterado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modelo_ia", x => x.id);
                    table.UniqueConstraint("ak_modelo_ia_empresa_id", x => new { x.empresa_id, x.id });
                    table.CheckConstraint("ck_modelo_ia_fator_fora_pico", "fator_fora_pico > 0 AND fator_fora_pico <= 1");
                    table.CheckConstraint("ck_modelo_ia_padrao_ligado", "NOT padrao OR ligado");
                    table.ForeignKey(
                        name: "fk_modelo_ia_provedor",
                        columns: x => new { x.empresa_id, x.provedor_id },
                        principalTable: "provedor_ia",
                        principalColumns: new[] { "empresa_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chamada_ia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modelo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    modelo_respondido = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    finalidade = table.Column<int>(type: "integer", nullable: false),
                    turno_agente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tokens_entrada = table.Column<int>(type: "integer", nullable: false),
                    tokens_saida = table.Column<int>(type: "integer", nullable: false),
                    tokens_cache_lido = table.Column<int>(type: "integer", nullable: false),
                    tokens_cache_escrito = table.Column<int>(type: "integer", nullable: false),
                    custo_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    duracao_ms = table.Column<int>(type: "integer", nullable: false),
                    desfecho = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chamada_ia", x => x.id);
                    table.ForeignKey(
                        name: "fk_chamada_ia_modelo",
                        columns: x => new { x.empresa_id, x.modelo_id },
                        principalTable: "modelo_ia",
                        principalColumns: new[] { "empresa_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chamada_ia_provedor",
                        columns: x => new { x.empresa_id, x.provedor_id },
                        principalTable: "provedor_ia",
                        principalColumns: new[] { "empresa_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configuracoes_atendimento_EmpresaId_ModeloIaId",
                table: "configuracoes_atendimento",
                columns: new[] { "EmpresaId", "ModeloIaId" });

            migrationBuilder.CreateIndex(
                name: "IX_chamada_ia_empresa_id_modelo_id",
                table: "chamada_ia",
                columns: new[] { "empresa_id", "modelo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chamada_ia_empresa_provedor_criado",
                table: "chamada_ia",
                columns: new[] { "empresa_id", "provedor_id", "criado_em" })
                .Annotation("Npgsql:IndexInclude", new[] { "custo_usd" });

            migrationBuilder.CreateIndex(
                name: "ux_modelo_ia_padrao_por_empresa",
                table: "modelo_ia",
                column: "empresa_id",
                unique: true,
                filter: "padrao");

            migrationBuilder.CreateIndex(
                name: "ux_modelo_ia_provedor_id_externo",
                table: "modelo_ia",
                columns: new[] { "empresa_id", "provedor_id", "id_externo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_provedor_ia_credencial",
                table: "provedor_ia",
                column: "credencial_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_configuracoes_atendimento_modelo_ia",
                table: "configuracoes_atendimento",
                columns: new[] { "EmpresaId", "ModeloIaId" },
                principalTable: "modelo_ia",
                principalColumns: new[] { "empresa_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // RLS (ADR-0010): colunas em snake_case, então a migration geral de RLS não casa; mesmo
            // predicado de credencial_integracao.
            migrationBuilder.Sql("""
ALTER TABLE provedor_ia ENABLE ROW LEVEL SECURITY;
ALTER TABLE provedor_ia FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON provedor_ia;
CREATE POLICY tenant_isolation ON provedor_ia
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR empresa_id = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    )
    WITH CHECK (
        current_setting('app.bypass_rls', true) = 'true'
        OR empresa_id = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    );
ALTER TABLE modelo_ia ENABLE ROW LEVEL SECURITY;
ALTER TABLE modelo_ia FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON modelo_ia;
CREATE POLICY tenant_isolation ON modelo_ia
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR empresa_id = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    )
    WITH CHECK (
        current_setting('app.bypass_rls', true) = 'true'
        OR empresa_id = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    );
ALTER TABLE chamada_ia ENABLE ROW LEVEL SECURITY;
ALTER TABLE chamada_ia FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON chamada_ia;
CREATE POLICY tenant_isolation ON chamada_ia
    USING (
        current_setting('app.bypass_rls', true) = 'true'
        OR empresa_id = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    )
    WITH CHECK (
        current_setting('app.bypass_rls', true) = 'true'
        OR empresa_id = NULLIF(current_setting('app.empresa_id', true), '')::uuid
    );
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
DROP POLICY IF EXISTS tenant_isolation ON chamada_ia;
DROP POLICY IF EXISTS tenant_isolation ON modelo_ia;
DROP POLICY IF EXISTS tenant_isolation ON provedor_ia;
""");

            migrationBuilder.DropForeignKey(
                name: "fk_configuracoes_atendimento_modelo_ia",
                table: "configuracoes_atendimento");

            migrationBuilder.DropTable(
                name: "chamada_ia");

            migrationBuilder.DropTable(
                name: "modelo_ia");

            migrationBuilder.DropTable(
                name: "provedor_ia");

            migrationBuilder.DropIndex(
                name: "IX_configuracoes_atendimento_EmpresaId_ModeloIaId",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "ModeloIaId",
                table: "configuracoes_atendimento");
        }
    }
}
