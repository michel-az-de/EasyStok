using System;

namespace EasyStok.Domain.Entities
{
    public class ItemVenda
    {
        public Guid Id { get; set; }
        public Guid VendaId { get; set; }
        public Guid ItemEstoqueId { get; set; }
        public Guid ProdutoId { get; set; }

        public string? DescricaoSnapshot { get; set; }
        public int Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal PrecoTotal { get; set; }
        public DateTime CriadoEm { get; set; }

        public Venda? Venda { get; set; }
        public ItemEstoque? ItemEstoque { get; set; }
        public Produto? Produto { get; set; }
    }
}
