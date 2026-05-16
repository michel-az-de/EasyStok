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
        // Inserido 2026-05-16 (correcao C2 / RDC 727/2022): peso obrigatorio
        // na etiqueta SO para Embalado. Default Avulso por seguranca.
        public TipoEmbalagem TipoEmbalagem { get; set; } = TipoEmbalagem.Avulso;
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

        // --- Receita / Calculadora de Producao (Onda 1.1) ---

        /// <summary>True se este produto aparece como insumo em alguma receita. Apenas filtro de UI — nao impede ter receita propria.</summary>
        public bool EhInsumo { get; set; }

        /// <summary>Quantidade-base que a receita produz (ex: rendimento 50 Un = receita rende 50 macarroes).</summary>
        public decimal RendimentoBase { get; set; } = 1m;

        /// <summary>Unidade do <see cref="RendimentoBase"/>.</summary>
        public UnidadeMedida RendimentoUnidade { get; set; } = UnidadeMedida.Un;

        /// <summary>Unidade em que ItemEstoque.QuantidadeAtual deste produto e mantido. Default Un — operador atualiza via Admin Web quando necessario.</summary>
        public UnidadeMedida UnidadeMedidaBase { get; set; } = UnidadeMedida.Un;

        public StatusProduto Status { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Guid? CriadoPor { get; set; }
        public Guid? AlteradoPor { get; set; }
        public string? ObservacaoInterna { get; set; }

        public Empresa? Empresa { get; set; }
        public Categoria? Categoria { get; set; }
        public Categoria? Subcategoria { get; set; }
        public ICollection<ProdutoCaracteristica> Caracteristicas { get; set; } = new List<ProdutoCaracteristica>();
        public ICollection<ProdutoEmbalagem> Embalagens { get; set; } = new List<ProdutoEmbalagem>();
        public ICollection<ProdutoVariacao> Variacoes { get; set; } = new List<ProdutoVariacao>();
        public ICollection<ItemEstoque> ItensEstoque { get; set; } = new List<ItemEstoque>();
        public ICollection<MovimentacaoEstoque> Movimentacoes { get; set; } = new List<MovimentacaoEstoque>();
        public ICollection<ItemVenda> ItensVenda { get; set; } = new List<ItemVenda>();

        /// <summary>Linhas de receita onde este produto e o produto-final.</summary>
        public ICollection<ProdutoComposicao> Composicoes { get; set; } = new List<ProdutoComposicao>();
    }
}
