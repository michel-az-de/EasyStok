namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Modelos de etiqueta globais do EasyStok — sem EmpresaId.
    /// Não editáveis por empresa; empresa duplica para criar versão custom.
    /// </summary>
    public class EtiquetaTemplateSistema
    {
        public Guid Id { get; set; }

        /// <summary>Identificador canônico: "identificacao" | "com-tabela-nutricional" | "refeicao-completa".</summary>
        public string Codigo { get; set; } = null!;

        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }

        /// <summary>JSON v=1 do layout canônico.</summary>
        public string LayoutJson { get; set; } = null!;

        /// <summary>Ordem de exibição na lista.</summary>
        public int Ordem { get; set; }
    }
}
