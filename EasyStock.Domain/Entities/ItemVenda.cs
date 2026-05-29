using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class ItemVenda
    {
        public Guid Id { get; set; }
        public Guid VendaId { get; set; }
        public Guid ItemEstoqueId { get; set; }
        public Guid ProdutoId { get; set; }
        public Guid? ProdutoVariacaoId { get; set; }

        public string? DescricaoSnapshot { get; set; }
        public string? VariacaoSnapshot { get; set; }
        public Quantidade Quantidade { get; set; } = null!;
        public Dinheiro PrecoUnitario { get; set; } = null!;
        public Dinheiro PrecoTotal { get; set; } = null!;
        public DateTime CriadoEm { get; set; }

        public Venda? Venda { get; set; }
        public ItemEstoque? ItemEstoque { get; set; }
        public Produto? Produto { get; set; }
        public ProdutoVariacao? ProdutoVariacao { get; set; }
    }
}
