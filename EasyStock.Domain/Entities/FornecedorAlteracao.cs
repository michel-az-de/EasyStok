using System;

namespace EasyStock.Domain.Entities
{
    /// <summary>
    /// Audit de alterações do <see cref="Fornecedor"/>. Onda P4 — espelho
    /// dos padrões <see cref="ProdutoAlteracao"/> e <see cref="ClienteAlteracao"/>:
    /// armazena diff campo-a-campo com quem alterou, quando e de onde
    /// (web/mobile/api).
    ///
    /// Queryable diretamente — usado pra mostrar timeline na tela de detalhe.
    /// </summary>
    public class FornecedorAlteracao
    {
        public Guid Id { get; set; }
        public Guid FornecedorId { get; set; }
        public Guid? AlteradoPorUserId { get; set; }
        public string? AlteradoPorNome { get; set; }
        public string Campo { get; set; } = null!;
        public string? ValorAntigo { get; set; }
        public string? ValorNovo { get; set; }
        public DateTime AlteradoEm { get; set; }
        public string? Origem { get; set; } // "web" | "mobile" | "api"

        public Fornecedor? Fornecedor { get; set; }
    }
}
