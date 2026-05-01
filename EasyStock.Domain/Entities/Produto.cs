using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class Produto
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid CategoriaId { get; set; }
        public Guid? SubcategoriaId { get; set; }
        public string Nome { get; set; } = null!;
        public string? DescricaoBase { get; set; }
        public string? Marca { get; set; }
        public TipoProduto Tipo { get; set; } // FISICO, ALIMENTO, SERVICO
        public CodigoSku? SkuBase { get; set; }
        public string? CodigoBarras { get; set; }
        public bool ControlaValidade { get; set; }

        public Dimensoes? Dimensoes { get; set; }

        public Dinheiro? CustoReferencia { get; set; }
        public Dinheiro? PrecoReferencia { get; set; }
        public decimal? MargemEstimada { get; set; }

        public string? AtributosJson { get; set; }
        public string? FotosJson { get; set; }
        public string? SugestaoDescricaoAnuncio { get; set; }

        // Overrides hierarquicos de limiar (null = herdar da Categoria/ConfiguracaoLoja/Default global).
        public int? QuantidadeMinima { get; set; }
        public int? QuantidadeCritica { get; set; }

        public StatusProduto Status { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Guid? CriadoPor { get; set; }
        public Guid? AlteradoPor { get; set; }
        public string? ObservacaoInterna { get; set; }

        public Empresa? Empresa { get; set; }
        public Categoria? Categoria { get; set; }
        public Categoria? Subcategoria { get; set; }
        public ICollection<ProdutoCaracteristica>? Caracteristicas { get; set; }
        public ICollection<ProdutoEmbalagem>? Embalagens { get; set; }
        public ICollection<ProdutoVariacao>? Variacoes { get; set; }
        public ICollection<ItemEstoque>? ItensEstoque { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
        public ICollection<ItemVenda>? ItensVenda { get; set; }
    }
}
