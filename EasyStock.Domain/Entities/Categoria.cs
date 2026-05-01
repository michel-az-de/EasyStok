using System;
using System.Collections.Generic;

namespace EasyStock.Domain.Entities
{
    public class Categoria
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid? CategoriaPaiId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }

        // Overrides hierarquicos de limiar (null = herdar da ConfiguracaoLoja/Default global).
        public int? QuantidadeMinima { get; set; }
        public int? QuantidadeCritica { get; set; }

        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Categoria? CategoriaPai { get; set; }
        public ICollection<Categoria>? SubCategorias { get; set; }
        public ICollection<Produto>? Produtos { get; set; }
    }
}
