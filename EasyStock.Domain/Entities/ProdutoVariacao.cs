using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class ProdutoVariacao
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ProdutoId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Cor { get; set; }
        public string? Tamanho { get; set; }
        public string? DescricaoComercial { get; set; }
        public CodigoSku? Sku { get; set; }
        public string? CodigoBarras { get; set; }
        public string? AtributosJson { get; set; }
        public Dimensoes? DimensoesPadrao { get; set; }
        public bool Ativa { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Produto? Produto { get; set; }
        public ICollection<ItemEstoque> ItensEstoque { get; set; } = new List<ItemEstoque>();
        public ICollection<ItemVenda> ItensVenda { get; set; } = new List<ItemVenda>();
        public ICollection<MovimentacaoEstoque> Movimentacoes { get; set; } = new List<MovimentacaoEstoque>();
    }
}
