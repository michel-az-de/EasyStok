using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class ItemEstoque
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ProdutoId { get; set; }
        public Guid? ProdutoVariacaoId { get; set; }

        public string? CodigoInterno { get; set; }
        public CodigoLote? CodigoLote { get; set; }
        public string? CodigoMarketplace { get; set; }
        public string? ChavePesquisa { get; set; }

        public string? VariacaoDescricao { get; set; }
        public string? Cor { get; set; }
        public string? Tamanho { get; set; }
        public string? DescricaoAnuncio { get; set; }

        public Dimensoes? DimensoesReais { get; set; }

        public string? FornecedorNome { get; set; }

        public Quantidade QuantidadeInicial { get; set; } = null!;
        public Quantidade QuantidadeAtual { get; set; } = null!;

        public Dinheiro CustoUnitario { get; set; } = null!;
        public Dinheiro? PrecoVendaSugerido { get; set; }

        public DateTime EntradaEm { get; set; }
        public Validade? ValidadeEm { get; set; }
        public DateTime? UltimaMovimentacaoEm { get; set; }

        public StatusItemEstoque Status { get; set; }
        public string? Observacoes { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Produto? Produto { get; set; }
        public ProdutoVariacao? ProdutoVariacao { get; set; }
        public ICollection<ItemVenda>? ItensVenda { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
    }
}
