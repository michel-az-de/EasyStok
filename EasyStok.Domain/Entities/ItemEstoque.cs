using System;
using System.Collections.Generic;

namespace EasyStok.Domain.Entities
{
    public class ItemEstoque
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ProdutoId { get; set; }

        public string? CodigoInterno { get; set; }
        public string? CodigoLote { get; set; }
        public string? CodigoMarketplace { get; set; }

        public string? VariacaoDescricao { get; set; }
        public string? Cor { get; set; }
        public string? Tamanho { get; set; }

        public decimal? PesoReal { get; set; }
        public decimal? LarguraReal { get; set; }
        public decimal? AlturaReal { get; set; }
        public decimal? ComprimentoReal { get; set; }

        public string? FornecedorNome { get; set; }

        public int QuantidadeInicial { get; set; }
        public int QuantidadeAtual { get; set; }

        public decimal CustoUnitario { get; set; }
        public decimal? PrecoVendaSugerido { get; set; }

        public DateTime EntradaEm { get; set; }
        public DateTime? ValidadeEm { get; set; }
        public DateTime? UltimaMovimentacaoEm { get; set; }

        public string Status { get; set; } = null!; // ATIVO, ESGOTADO, VENCIDO, DESCARTADO
        public string? Observacoes { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Produto? Produto { get; set; }
        public ICollection<ItemVenda>? ItensVenda { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
    }
}
