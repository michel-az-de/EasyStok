using System;
using System.Collections.Generic;

namespace EasyStok.Domain.Entities
{
    public class Produto
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public Guid CategoriaId { get; set; }
        public string Nome { get; set; } = null!;
        public string? DescricaoBase { get; set; }
        public string? Marca { get; set; }
        public string Tipo { get; set; } = null!; // FISICO, ALIMENTO, SERVICO
        public string? SkuBase { get; set; }
        public string? CodigoBarras { get; set; }
        public bool ControlaValidade { get; set; }

        public decimal? Peso { get; set; }
        public decimal? Largura { get; set; }
        public decimal? Altura { get; set; }
        public decimal? Comprimento { get; set; }

        public decimal? CustoReferencia { get; set; }
        public decimal? PrecoReferencia { get; set; }
        public decimal? MargemEstimada { get; set; }

        public string? AtributosJson { get; set; }
        public string? FotosJson { get; set; }
        public string? EmbalagemJson { get; set; }

        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime AlteradoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Categoria? Categoria { get; set; }
        public ICollection<ItemEstoque>? ItensEstoque { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
        public ICollection<ItemVenda>? ItensVenda { get; set; }
    }
}
