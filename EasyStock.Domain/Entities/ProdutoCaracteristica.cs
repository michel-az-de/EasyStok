namespace EasyStock.Domain.Entities
{
    public class ProdutoCaracteristica
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ProdutoId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }
        public int? QuantidadeReferencia { get; set; }
        public string? VariacaoPadrao { get; set; }
        public Guid? VariacaoId { get; set; }
        public int OrdemExibicao { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Produto? Produto { get; set; }
        public ProdutoVariacao? Variacao { get; set; }
    }
}
