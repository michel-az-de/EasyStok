using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddCorridasExternas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corridas_externas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViagemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provedor = table.Column<int>(type: "integer", nullable: false),
                    Origem = table.Column<int>(type: "integer", nullable: false),
                    CustoCentavos = table.Column<int>(type: "integer", nullable: false),
                    Motorista = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RegistradoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrigidoPorUsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrigidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corridas_externas", x => x.Id);
                    table.CheckConstraint("ck_corridas_externas_custo", "\"CustoCentavos\" >= 0");
                    table.ForeignKey(
                        name: "FK_corridas_externas_viagens_ViagemId",
                        column: x => x.ViagemId,
                        principalTable: "viagens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corridas_externas_empresa_viagem",
                table: "corridas_externas",
                columns: new[] { "EmpresaId", "ViagemId" });

            migrationBuilder.CreateIndex(
                name: "IX_corridas_externas_ViagemId",
                table: "corridas_externas",
                column: "ViagemId");

            // RLS (ADR-0010, defesa em profundidade): mesmo padrão de AddEntregadoresViagens para
            // tabela nova com EmpresaId.
            migrationBuilder.Sql("""
DO $rls$
BEGIN
    EXECUTE format('ALTER TABLE %I.corridas_externas ENABLE ROW LEVEL SECURITY', current_schema());
    EXECUTE format('ALTER TABLE %I.corridas_externas FORCE ROW LEVEL SECURITY', current_schema());
    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I.corridas_externas', current_schema());
    EXECUTE format($pol$
        CREATE POLICY tenant_isolation ON %I.corridas_externas
            USING (
                current_setting('app.bypass_rls', true) = 'true'
                OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
            )
            WITH CHECK (
                current_setting('app.bypass_rls', true) = 'true'
                OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
            )
    $pol$, current_schema());
END
$rls$;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corridas_externas");
        }
    }
}
