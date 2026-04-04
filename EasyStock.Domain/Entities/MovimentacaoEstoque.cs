using System;
using EasyStock.Domain.ValueObjects;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    public class MovimentacaoEstoque
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ItemEstoqueId { get; set; }
        public Guid ProdutoId { get; set; }
        public Guid? ProdutoVariacaoId { get; set; }
        public Guid? VendaId { get; set; }

        public TipoMovimentacaoEstoque Tipo { get; set; }
        public NaturezaMovimentacaoEstoque Natureza { get; set; }
        public Quantidade Quantidade { get; set; } = null!;

        public Dinheiro? ValorUnitario { get; set; }
        public Dinheiro? ValorTotal { get; set; }

        public DateTime DataMovimentacao { get; set; }
        public string? Descricao { get; set; }
        public string? DocumentoReferencia { get; set; }
        public DateTime CriadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public ItemEstoque? ItemEstoque { get; set; }
        public Produto? Produto { get; set; }
        public ProdutoVariacao? ProdutoVariacao { get; set; }
        public Venda? Venda { get; set; }
    }
}
