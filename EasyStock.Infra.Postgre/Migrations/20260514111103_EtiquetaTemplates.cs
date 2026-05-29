using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class EtiquetaTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "lote_etiquetas",
                type: "character varying(25)",
                maxLength: 25,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "LayoutSnapshotJson",
                table: "lote_etiquetas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LayoutSnapshotMeta",
                table: "lote_etiquetas",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "etiqueta_empresa_default",
                columns: table => new
                {
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateOrigem = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_etiqueta_empresa_default", x => x.EmpresaId);
                    table.ForeignKey(
                        name: "FK_etiqueta_empresa_default_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "etiqueta_templates_sistema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    LayoutJson = table.Column<string>(type: "text", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_etiqueta_templates_sistema", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "etiqueta_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BaseSistemaId = table.Column<Guid>(type: "uuid", nullable: true),
                    LayoutJson = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_etiqueta_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_etiqueta_templates_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_etiqueta_templates_etiqueta_templates_sistema_BaseSistemaId",
                        column: x => x.BaseSistemaId,
                        principalTable: "etiqueta_templates_sistema",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_etiqueta_templates_BaseSistemaId",
                table: "etiqueta_templates",
                column: "BaseSistemaId");

            migrationBuilder.CreateIndex(
                name: "IX_etiqueta_templates_EmpresaId_Nome",
                table: "etiqueta_templates",
                columns: new[] { "EmpresaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_etiqueta_templates_sistema_Codigo",
                table: "etiqueta_templates_sistema",
                column: "Codigo",
                unique: true);

            // CHECK constraint para status válidos em lote_etiquetas
            migrationBuilder.Sql(@"
                ALTER TABLE lote_etiquetas
                DROP CONSTRAINT IF EXISTS lote_etiquetas_status_check;

                ALTER TABLE lote_etiquetas
                ADD CONSTRAINT lote_etiquetas_status_check
                CHECK (""Status"" IN ('pendente','enviada_impressao','impressa','conferida','divergente','consumida'));
            ");

            // Seed — 3 modelos prontos do sistema
            var idIdentificacao      = new Guid("10000000-0000-0000-0000-000000000001");
            var idNutricional        = new Guid("10000000-0000-0000-0000-000000000002");
            var idRefeicao           = new Guid("10000000-0000-0000-0000-000000000003");

            var layoutIdentificacao = """
                {"v":1,"size":{"preset":"80x40mm","w_mm":80,"h_mm":40,"orientation":"horizontal"},"elements":[
                  {"id":"logo","type":"image","asset":"system:lockup-easystok","x_mm":2,"y_mm":2,"w_mm":10,"h_mm":5,"locked":false},
                  {"id":"nome","type":"text","content":"{produto.nome}","x_mm":14,"y_mm":2,"w_mm":44,"h_mm":8,"font":"sans","size_pt":14,"weight":700,"align":"left","overflow":"shrink-then-ellipsis"},
                  {"id":"marca","type":"text","content":"{produto.marca}","x_mm":14,"y_mm":11,"w_mm":44,"h_mm":5,"font":"sans","size_pt":9,"weight":400,"align":"left","overflow":"shrink-then-ellipsis"},
                  {"id":"div-zonas","type":"divider","x_mm":2,"y_mm":19,"w_mm":56,"h_mm":1,"stroke_pt":0.5},
                  {"id":"lote","type":"text","content":"LOT {lote.codigo}","x_mm":2,"y_mm":26,"w_mm":56,"h_mm":4,"font":"mono","size_pt":9,"weight":700,"align":"left","overflow":"clip"},
                  {"id":"val","type":"text","content":"VAL {lote.validadeEm:dd/MM/yyyy}","x_mm":2,"y_mm":31,"w_mm":56,"h_mm":5,"font":"mono","size_pt":9,"weight":400,"align":"left","overflow":"clip"},
                  {"id":"qr","type":"code","format":"qr","content":"{etiqueta.codigo}","x_mm":60,"y_mm":4,"w_mm":18,"h_mm":18,"quiet_zone_mm":1},
                  {"id":"seq","type":"text","content":"{etiqueta.sequencial}","x_mm":60,"y_mm":22,"w_mm":18,"h_mm":5,"font":"mono","size_pt":8,"weight":400,"align":"center","overflow":"clip"},
                  {"id":"footer","type":"text","content":"@easystok","x_mm":62,"y_mm":35,"w_mm":16,"h_mm":3,"font":"sans","size_pt":6,"weight":400,"align":"center","color":"ink-500","locked":false}
                ]}
                """;

            var layoutNutricional = """
                {"v":1,"size":{"preset":"80x40mm","w_mm":80,"h_mm":40,"orientation":"horizontal"},"elements":[
                  {"id":"logo","type":"image","asset":"system:logo-easystok","x_mm":2,"y_mm":2,"w_mm":6,"h_mm":6,"locked":false},
                  {"id":"nome","type":"text","content":"{produto.nome}","x_mm":12,"y_mm":2,"w_mm":40,"h_mm":7,"font":"sans","size_pt":11,"weight":700,"align":"left","overflow":"shrink-then-ellipsis"},
                  {"id":"lote","type":"text","content":"LOT {lote.codigo}","x_mm":12,"y_mm":10,"w_mm":40,"h_mm":4,"font":"mono","size_pt":8,"weight":700,"align":"left","overflow":"clip"},
                  {"id":"val","type":"text","content":"VAL {lote.validadeEm:dd/MM/yyyy}","x_mm":12,"y_mm":15,"w_mm":40,"h_mm":4,"font":"mono","size_pt":8,"weight":400,"align":"left","overflow":"clip"},
                  {"id":"nutri","type":"nutritional-table","x_mm":2,"y_mm":20,"w_mm":52,"h_mm":16,"size_pt_min":6,"size_pt_max":8},
                  {"id":"qr","type":"code","format":"qr","content":"{etiqueta.codigo}","x_mm":60,"y_mm":2,"w_mm":18,"h_mm":18,"quiet_zone_mm":1},
                  {"id":"empresa","type":"text","content":"{empresa.nome}","x_mm":56,"y_mm":22,"w_mm":22,"h_mm":4,"font":"sans","size_pt":7,"weight":400,"align":"center","overflow":"shrink-then-ellipsis"},
                  {"id":"footer","type":"text","content":"@easystok","x_mm":62,"y_mm":35,"w_mm":16,"h_mm":3,"font":"sans","size_pt":6,"weight":400,"align":"center","color":"ink-500","locked":false}
                ]}
                """;

            var layoutRefeicao = """
                {"v":1,"size":{"preset":"80x40mm","w_mm":80,"h_mm":40,"orientation":"horizontal"},"elements":[
                  {"id":"logo","type":"image","asset":"system:logo-easystok","x_mm":2,"y_mm":2,"w_mm":6,"h_mm":6,"locked":false},
                  {"id":"nome","type":"text","content":"{produto.nome}","x_mm":12,"y_mm":2,"w_mm":40,"h_mm":6,"font":"sans","size_pt":11,"weight":700,"align":"left","overflow":"shrink-then-ellipsis"},
                  {"id":"val","type":"text","content":"VAL {lote.validadeEm:dd/MM/yyyy}","x_mm":12,"y_mm":9,"w_mm":40,"h_mm":4,"font":"mono","size_pt":8,"weight":700,"align":"left","overflow":"clip"},
                  {"id":"nutri","type":"nutritional-table","x_mm":2,"y_mm":14,"w_mm":46,"h_mm":16,"size_pt_min":6,"size_pt_max":8},
                  {"id":"alergenos","type":"alergenos-pills","x_mm":2,"y_mm":31,"w_mm":54,"h_mm":4},
                  {"id":"qr","type":"code","format":"qr","content":"{etiqueta.codigo}","x_mm":60,"y_mm":2,"w_mm":18,"h_mm":18,"quiet_zone_mm":1},
                  {"id":"lote","type":"text","content":"LOT {lote.codigo}","x_mm":56,"y_mm":22,"w_mm":22,"h_mm":4,"font":"mono","size_pt":7,"weight":400,"align":"center","overflow":"clip"},
                  {"id":"empresa","type":"text","content":"{empresa.nome}","x_mm":56,"y_mm":27,"w_mm":22,"h_mm":4,"font":"sans","size_pt":7,"weight":400,"align":"center","overflow":"shrink-then-ellipsis"},
                  {"id":"footer","type":"text","content":"@easystok","x_mm":62,"y_mm":35,"w_mm":16,"h_mm":3,"font":"sans","size_pt":6,"weight":400,"align":"center","color":"ink-500","locked":false}
                ]}
                """;

            migrationBuilder.InsertData(
                table: "etiqueta_templates_sistema",
                columns: ["Id", "Codigo", "Nome", "Descricao", "LayoutJson", "Ordem"],
                values: new object[]
                {
                    idIdentificacao, "identificacao", "Identificação",
                    "Compacta. Logo + nome + lote + QR. Sem nutrição. Rastreio interno.",
                    layoutIdentificacao, 1
                });

            migrationBuilder.InsertData(
                table: "etiqueta_templates_sistema",
                columns: ["Id", "Codigo", "Nome", "Descricao", "LayoutJson", "Ordem"],
                values: new object[]
                {
                    idNutricional, "com-tabela-nutricional", "Com tabela nutricional",
                    "Identificação + tabela ANVISA-friendly. Embalagem ao cliente.",
                    layoutNutricional, 2
                });

            migrationBuilder.InsertData(
                table: "etiqueta_templates_sistema",
                columns: ["Id", "Codigo", "Nome", "Descricao", "LayoutJson", "Ordem"],
                values: new object[]
                {
                    idRefeicao, "refeicao-completa", "Refeição completa",
                    "Identificação + nutrição + alérgenos. Refeições prontas.",
                    layoutRefeicao, 3
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "etiqueta_empresa_default");

            migrationBuilder.DropTable(
                name: "etiqueta_templates");

            migrationBuilder.DropTable(
                name: "etiqueta_templates_sistema");

            migrationBuilder.DropColumn(
                name: "LayoutSnapshotJson",
                table: "lote_etiquetas");

            migrationBuilder.DropColumn(
                name: "LayoutSnapshotMeta",
                table: "lote_etiquetas");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "lote_etiquetas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(25)",
                oldMaxLength: 25);
        }
    }
}
