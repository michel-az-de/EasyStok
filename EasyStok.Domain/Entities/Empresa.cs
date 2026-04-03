using System;
using System.Collections.Generic;

namespace EasyStok.Domain.Entities
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
        public ICollection<ItemEstoque>? ItensEstoque { get; set; }
        public ICollection<Venda>? Vendas { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
    }
}
