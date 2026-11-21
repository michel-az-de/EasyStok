using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// #2199 (spec impressão v2): regra de impressão da loja (D2) e número curto do lote (D4).
    /// O número curto vem do banco: o contador por empresa e loja (<c>lote_numero_contador</c>, com RLS) e o gatilho
    /// <c>trg_lotes_numero_curto</c> numeram todo lote novo, venha da API, do sync do aparelho ou de SQL. Lote antigo
    /// não é renumerado aqui; ganha o número na primeira impressão (<c>LoteNumeroCurtoRepository</c>).
    /// </remarks>
    public partial class ImpressaoDaLojaENumeroCurtoDoLote : Migration
    {
        private const string RlsContador = """
            ALTER TABLE public.lote_numero_contador ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.lote_numero_contador FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON public.lote_numero_contador;
            CREATE POLICY tenant_isolation ON public.lote_numero_contador
                USING (current_setting('app.bypass_rls', true) = 'true'
                       OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)
                WITH CHECK (current_setting('app.bypass_rls', true) = 'true'
                       OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid);
            """;

        // Próximo número da loja (lote sem loja conta na chave zero). 8 dígitos até 99.999.999 e 10 depois: Code128 C
        // codifica pares, então o tamanho é sempre par. Roda como quem chama, sob o RLS do contador.
        private const string FuncaoProximo = """
            CREATE OR REPLACE FUNCTION public.lote_numero_curto_proximo(p_empresa uuid, p_loja uuid)
            RETURNS character varying LANGUAGE plpgsql VOLATILE AS $$
            DECLARE n bigint;
            BEGIN
                INSERT INTO public.lote_numero_contador AS c ("EmpresaId", "LojaId", "Ultimo")
                VALUES (p_empresa, COALESCE(p_loja, '00000000-0000-0000-0000-000000000000'::uuid), 1)
                ON CONFLICT ("EmpresaId", "LojaId") DO UPDATE SET "Ultimo" = c."Ultimo" + 1
                RETURNING "Ultimo" INTO n;
                IF n > 9999999999 THEN
                    RAISE EXCEPTION 'numero curto do lote esgotado para a loja %', p_loja;
                END IF;
                RETURN lpad(n::text, CASE WHEN n <= 99999999 THEN 8 ELSE 10 END, '0');
            END $$;
            """;

        private const string Gatilho = """
            CREATE OR REPLACE FUNCTION public.lotes_numero_curto_preencher()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                NEW."NumeroCurto" := public.lote_numero_curto_proximo(NEW."EmpresaId", NEW."LojaId");
                RETURN NEW;
            END $$;

            DROP TRIGGER IF EXISTS trg_lotes_numero_curto ON public.lotes;
            CREATE TRIGGER trg_lotes_numero_curto BEFORE INSERT ON public.lotes
                FOR EACH ROW WHEN (NEW."NumeroCurto" IS NULL)
                EXECUTE FUNCTION public.lotes_numero_curto_preencher();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NumeroCurto",
                table: "lotes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImpressaoAlteradaEm",
                table: "configuracoes_loja",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImpressaoJson",
                table: "configuracoes_loja",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImpressaoVersao",
                table: "configuracoes_loja",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "lote_numero_contador",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    LojaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ultimo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lote_numero_contador", x => new { x.EmpresaId, x.LojaId });
                    table.ForeignKey(
                        name: "FK_lote_numero_contador_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lotes_EmpresaId_LojaId_NumeroCurto",
                table: "lotes",
                columns: new[] { "EmpresaId", "LojaId", "NumeroCurto" },
                unique: true);

            migrationBuilder.Sql(RlsContador);
            migrationBuilder.Sql(FuncaoProximo);
            migrationBuilder.Sql(Gatilho);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_lotes_numero_curto ON public.lotes;
                DROP FUNCTION IF EXISTS public.lotes_numero_curto_preencher();
                DROP FUNCTION IF EXISTS public.lote_numero_curto_proximo(uuid, uuid);
                DROP POLICY IF EXISTS tenant_isolation ON public.lote_numero_contador;
                ALTER TABLE public.lote_numero_contador NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE public.lote_numero_contador DISABLE ROW LEVEL SECURITY;
                """);

            migrationBuilder.DropTable(
                name: "lote_numero_contador");

            migrationBuilder.DropIndex(
                name: "IX_lotes_EmpresaId_LojaId_NumeroCurto",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "NumeroCurto",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "ImpressaoAlteradaEm",
                table: "configuracoes_loja");

            migrationBuilder.DropColumn(
                name: "ImpressaoJson",
                table: "configuracoes_loja");

            migrationBuilder.DropColumn(
                name: "ImpressaoVersao",
                table: "configuracoes_loja");
        }
    }
}
