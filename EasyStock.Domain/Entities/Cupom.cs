using System;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Entities
{
    public class Cupom
    {
        public Guid Id { get; private set; }
        public string Codigo { get; private set; } = null!;
        public TipoDesconto TipoDesconto { get; private set; }
        public decimal Valor { get; private set; }
        public int? LimiteUsos { get; private set; }
        public int TotalUsos { get; private set; }
        public DateTime? ValidoAte { get; private set; }
        public Guid? PlanoId { get; private set; }
        public bool Ativo { get; private set; }
        public DateTime CriadoEm { get; private set; }

        private Cupom() { }

        public static Cupom Criar(string codigo, TipoDesconto tipo, decimal valor,
                                   int? limiteUsos, DateTime? validoAte, Guid? planoId)
            => new()
            {
                Id = Guid.NewGuid(),
                Codigo = codigo.ToUpperInvariant(),
                TipoDesconto = tipo,
                Valor = valor,
                LimiteUsos = limiteUsos,
                ValidoAte = validoAte,
                PlanoId = planoId,
                Ativo = true,
                TotalUsos = 0,
                CriadoEm = DateTime.UtcNow
            };

        public bool PodeUsarEm(DateTime agora) =>
            Ativo && (LimiteUsos == null || TotalUsos < LimiteUsos) && (ValidoAte == null || agora <= ValidoAte);

        public void IncrementarUso() => TotalUsos++;

        public void Toggle() => Ativo = !Ativo;

        public void Atualizar(string? codigo, TipoDesconto? tipo, decimal? valor,
                               int? limiteUsos, DateTime? validoAte, Guid? planoId)
        {
            if (codigo != null) Codigo = codigo.ToUpperInvariant();
            if (tipo.HasValue) TipoDesconto = tipo.Value;
            if (valor.HasValue) Valor = valor.Value;
            LimiteUsos = limiteUsos;
            ValidoAte = validoAte;
            PlanoId = planoId;
        }
    }
}
