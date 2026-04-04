using System;
using System.Collections.Generic;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Entities
{
    public class Venda
    {
        public Guid Id { get; set; }
        public Guid EmpresaId { get; set; }
        public CanalVenda Canal { get; set; }
        public NaturezaMovimentacaoEstoque Natureza { get; set; }
        public DateTime DataVenda { get; set; }
        public DateTime? DataEnvio { get; set; }
        public string? NumeroNotaFiscal { get; set; }
        public Dinheiro ValorTotal { get; set; } = null!;
        public string? Observacoes { get; set; }
        public DateTime CriadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public ICollection<ItemVenda>? ItensVenda { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }
    }
}
