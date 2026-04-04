using System;

namespace EasyStock.Domain.Entities
{
    public class Plano
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string? Descricao { get; set; }
        public int LimiteLojas { get; set; }
        public int LimiteUsuarios { get; set; }
        public int LimiteProdutos { get; set; }
        public decimal PrecoMensal { get; set; }
        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }
    }
}
