using System;

namespace EasyStok.Domain.Entities
{
    public class MovimentacaoEstoque
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ItemEstoqueId { get; set; }
        public Guid ProdutoId { get; set; }
        public Guid? VendaId { get; set; }

        public string Tipo { get; set; } = null!; // ENTRADA, SAIDA, AJUSTE
        public string Natureza { get; set; } = null!; // COMPRA, VENDA, PERDA...
        public int Quantidade { get; set; }

        public decimal? ValorUnitario { get; set; }
        public decimal? ValorTotal { get; set; }

        public DateTime DataMovimentacao { get; set; }
        public string? Descricao { get; set; }
        public string? DocumentoReferencia { get; set; }
        public DateTime CriadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public ItemEstoque? ItemEstoque { get; set; }
        public Produto? Produto { get; set; }
        public Venda? Venda { get; set; }
    }
}
