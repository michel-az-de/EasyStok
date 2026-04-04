using System;
using System.Collections.Generic;

namespace EasyStock.Domain.Entities
{
    public class Empresa
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string? Documento { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public ICollection<Categoria>? Categorias { get; set; }
        public ICollection<Produto>? Produtos { get; set; }
        public ICollection<ProdutoVariacao>? VariacoesProduto { get; set; }
        public ICollection<ProdutoCaracteristica>? CaracteristicasProduto { get; set; }
        public ICollection<ProdutoEmbalagem>? EmbalagensProduto { get; set; }
        public ICollection<ItemEstoque>? ItensEstoque { get; set; }
        public ICollection<Venda>? Vendas { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
    }
}
