using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities
{
    public class Empresa
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string? Documento { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }
        /// <summary>
        /// Marcador de seed — [NotMapped] pois a coluna não existe em produção
        /// (migration ficou vazia). Cleanup usa documento fixo via SeedDocumentos[].
        /// </summary>
        [NotMapped]
        public bool IsSeedData { get; set; }

        /// <summary>
        /// Feature flag do modulo Financeiro (F1+). Default false para tenants
        /// existentes (Casa da Baba nao deve ver menu Financeiro ate ser
        /// explicitamente habilitado). Default true para tenants criados apos
        /// a migration que introduziu a coluna.
        /// </summary>
        public bool FinanceiroHabilitado { get; set; } = false;

        public static Empresa Criar(string nome, string? documento)
        {
            var agora = DateTime.UtcNow;
            return new Empresa
            {
                Id = Guid.NewGuid(),
                Nome = nome.Trim(),
                Documento = documento?.Trim(),
                CriadoEm = agora,
                AlteradoEm = agora
            };
        }

        public ICollection<Categoria>? Categorias { get; set; }
        public ICollection<Produto>? Produtos { get; set; }
        public ICollection<ProdutoVariacao>? VariacoesProduto { get; set; }
        public ICollection<ProdutoCaracteristica>? CaracteristicasProduto { get; set; }
        public ICollection<ProdutoEmbalagem>? EmbalagensProduto { get; set; }
        public ICollection<ItemEstoque>? ItensEstoque { get; set; }
        public ICollection<Venda>? Vendas { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
        public ICollection<Loja>? Lojas { get; set; }
    }
}
