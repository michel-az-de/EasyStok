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

        public ICollection<Categoria> Categorias { get; set; } = new List<Categoria>();
        public ICollection<Produto> Produtos { get; set; } = new List<Produto>();
        public ICollection<ProdutoVariacao> VariacoesProduto { get; set; } = new List<ProdutoVariacao>();
        public ICollection<ProdutoCaracteristica> CaracteristicasProduto { get; set; } = new List<ProdutoCaracteristica>();
        public ICollection<ProdutoEmbalagem> EmbalagensProduto { get; set; } = new List<ProdutoEmbalagem>();
        public ICollection<ItemEstoque> ItensEstoque { get; set; } = new List<ItemEstoque>();
        public ICollection<Venda> Vendas { get; set; } = new List<Venda>();
        public ICollection<MovimentacaoEstoque> Movimentacoes { get; set; } = new List<MovimentacaoEstoque>();
        public ICollection<Loja> Lojas { get; set; } = new List<Loja>();
    }
}
