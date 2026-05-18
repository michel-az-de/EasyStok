using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class EtiquetaTemplatesRedesign : Migration
    {
        // Identificação — logo lockup + nome bold + marca gray + divider + FAB/VAL/LOTE | QR direita
        private const string LayoutIdentificacao =
            """{"v":1,"size":{"preset":"80x40mm","w_mm":80,"h_mm":40,"orientation":"horizontal"},"elements":[{"id":"logo","type":"image","asset":"system:lockup-easystok","x_mm":2,"y_mm":2,"w_mm":24,"h_mm":7,"locked":false},{"id":"nome","type":"text","content":"{produto.nome}","x_mm":2,"y_mm":11,"w_mm":37,"h_mm":8,"font":"sans","size_pt":12,"weight":700,"align":"left","overflow":"shrink-then-ellipsis"},{"id":"marca","type":"text","content":"{produto.marca}","x_mm":2,"y_mm":20,"w_mm":37,"h_mm":4,"font":"sans","size_pt":8,"weight":400,"align":"left","color":"ink-500","overflow":"shrink-then-ellipsis"},{"id":"div","type":"divider","x_mm":2,"y_mm":25,"w_mm":37,"h_mm":0.5,"stroke_pt":0.5},{"id":"fab","type":"text","content":"FAB {lote.criadoEm:dd/MM/yyyy}","x_mm":2,"y_mm":27,"w_mm":37,"h_mm":3.5,"font":"mono","size_pt":8,"weight":400,"align":"left","overflow":"clip"},{"id":"val","type":"text","content":"VAL {lote.validadeEm:dd/MM/yyyy}","x_mm":2,"y_mm":31,"w_mm":37,"h_mm":3.5,"font":"mono","size_pt":8,"weight":400,"align":"left","overflow":"clip"},{"id":"lote","type":"text","content":"LOTE {lote.codigo}","x_mm":2,"y_mm":35,"w_mm":37,"h_mm":4,"font":"mono","size_pt":8,"weight":700,"align":"left","overflow":"clip"},{"id":"qr","type":"code","format":"qr","content":"{etiqueta.codigo}","x_mm":42,"y_mm":2,"w_mm":18,"h_mm":18,"quiet_zone_mm":1},{"id":"seq","type":"text","content":"{etiqueta.sequencial}","x_mm":42,"y_mm":21,"w_mm":18,"h_mm":4,"font":"mono","size_pt":8,"weight":400,"align":"center","overflow":"clip"},{"id":"footer","type":"text","content":"@easystok","x_mm":62,"y_mm":36,"w_mm":16,"h_mm":3,"font":"sans","size_pt":6,"weight":400,"align":"center","color":"ink-500","locked":false}]}""";

        // Com tabela nutricional — logo ícone + nome + FAB/VAL | QR direita | nutri table fundo
        private const string LayoutNutricional =
            """{"v":1,"size":{"preset":"80x40mm","w_mm":80,"h_mm":40,"orientation":"horizontal"},"elements":[{"id":"logo","type":"image","asset":"system:logo-easystok","x_mm":2,"y_mm":2,"w_mm":7,"h_mm":7,"locked":false},{"id":"nome","type":"text","content":"{produto.nome}","x_mm":11,"y_mm":2,"w_mm":34,"h_mm":7,"font":"sans","size_pt":11,"weight":700,"align":"left","overflow":"shrink-then-ellipsis"},{"id":"fab","type":"text","content":"FAB {lote.criadoEm:dd/MM/yyyy}","x_mm":11,"y_mm":10,"w_mm":34,"h_mm":3.5,"font":"mono","size_pt":8,"weight":400,"align":"left","overflow":"clip"},{"id":"val","type":"text","content":"VAL {lote.validadeEm:dd/MM/yyyy}","x_mm":11,"y_mm":14,"w_mm":34,"h_mm":3.5,"font":"mono","size_pt":8,"weight":400,"align":"left","overflow":"clip"},{"id":"div","type":"divider","x_mm":2,"y_mm":19,"w_mm":54,"h_mm":0.5,"stroke_pt":0.5},{"id":"nutri","type":"nutritional-table","x_mm":2,"y_mm":20.5,"w_mm":54,"h_mm":16.5,"size_pt_min":6,"size_pt_max":8},{"id":"qr","type":"code","format":"qr","content":"{etiqueta.codigo}","x_mm":59,"y_mm":2,"w_mm":19,"h_mm":19,"quiet_zone_mm":1},{"id":"lote","type":"text","content":"LOTE {lote.codigo}","x_mm":59,"y_mm":22,"w_mm":19,"h_mm":3.5,"font":"mono","size_pt":7,"weight":700,"align":"center","overflow":"clip"},{"id":"empresa","type":"text","content":"{empresa.nome}","x_mm":59,"y_mm":26.5,"w_mm":19,"h_mm":3.5,"font":"sans","size_pt":7,"weight":400,"align":"center","overflow":"shrink-then-ellipsis"},{"id":"footer","type":"text","content":"@easystok","x_mm":59,"y_mm":36,"w_mm":19,"h_mm":3,"font":"sans","size_pt":6,"weight":400,"align":"center","color":"ink-500","locked":false}]}""";

        // Refeição completa — logo + nome + VAL destaque + FAB | QR direita | nutri + alérgenos fundo
        private const string LayoutRefeicao =
            """{"v":1,"size":{"preset":"80x40mm","w_mm":80,"h_mm":40,"orientation":"horizontal"},"elements":[{"id":"logo","type":"image","asset":"system:logo-easystok","x_mm":2,"y_mm":2,"w_mm":7,"h_mm":7,"locked":false},{"id":"nome","type":"text","content":"{produto.nome}","x_mm":11,"y_mm":2,"w_mm":34,"h_mm":6,"font":"sans","size_pt":11,"weight":700,"align":"left","overflow":"shrink-then-ellipsis"},{"id":"val","type":"text","content":"VAL {lote.validadeEm:dd/MM/yyyy}","x_mm":11,"y_mm":9,"w_mm":34,"h_mm":3.5,"font":"mono","size_pt":8,"weight":700,"align":"left","overflow":"clip"},{"id":"fab","type":"text","content":"FAB {lote.criadoEm:dd/MM/yyyy}","x_mm":11,"y_mm":13,"w_mm":34,"h_mm":3,"font":"mono","size_pt":7.5,"weight":400,"align":"left","overflow":"clip"},{"id":"div","type":"divider","x_mm":2,"y_mm":17.5,"w_mm":54,"h_mm":0.5,"stroke_pt":0.5},{"id":"nutri","type":"nutritional-table","x_mm":2,"y_mm":18.5,"w_mm":52,"h_mm":12,"size_pt_min":6,"size_pt_max":8},{"id":"alergenos","type":"alergenos-pills","x_mm":2,"y_mm":32,"w_mm":54,"h_mm":4},{"id":"qr","type":"code","format":"qr","content":"{etiqueta.codigo}","x_mm":59,"y_mm":2,"w_mm":19,"h_mm":19,"quiet_zone_mm":1},{"id":"lote","type":"text","content":"LOTE {lote.codigo}","x_mm":59,"y_mm":22,"w_mm":19,"h_mm":3.5,"font":"mono","size_pt":7,"weight":700,"align":"center","overflow":"clip"},{"id":"empresa","type":"text","content":"{empresa.nome}","x_mm":59,"y_mm":26.5,"w_mm":19,"h_mm":3.5,"font":"sans","size_pt":7,"weight":400,"align":"center","overflow":"shrink-then-ellipsis"},{"id":"footer","type":"text","content":"@easystok","x_mm":59,"y_mm":36,"w_mm":19,"h_mm":3,"font":"sans","size_pt":6,"weight":400,"align":"center","color":"ink-500","locked":false}]}""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"""
                UPDATE etiqueta_templates_sistema
                SET "LayoutJson" = '{LayoutIdentificacao.Replace("'", "''")}'
                WHERE "Id" = '10000000-0000-0000-0000-000000000001';
                """);

            migrationBuilder.Sql(
                $"""
                UPDATE etiqueta_templates_sistema
                SET "LayoutJson" = '{LayoutNutricional.Replace("'", "''")}'
                WHERE "Id" = '10000000-0000-0000-0000-000000000002';
                """);

            migrationBuilder.Sql(
                $"""
                UPDATE etiqueta_templates_sistema
                SET "LayoutJson" = '{LayoutRefeicao.Replace("'", "''")}'
                WHERE "Id" = '10000000-0000-0000-0000-000000000003';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down não restaura layout anterior — rollback manual se necessário.
        }
    }
}
