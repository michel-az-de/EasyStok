using System;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class ProdutoEmbalagem
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid ProdutoId { get; set; }
        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }
        public Dimensoes? Dimensoes { get; set; }
        public bool Padrao { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Produto? Produto { get; set; }
    }
}
