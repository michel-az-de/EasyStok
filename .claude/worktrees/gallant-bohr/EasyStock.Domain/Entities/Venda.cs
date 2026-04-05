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
        public Guid? LojaId { get; set; }
        public CanalVenda Canal { get; set; }
        public NaturezaMovimentacaoEstoque Natureza { get; set; }
        public DateTime DataVenda { get; set; }
        public DateTime? DataEnvio { get; set; }
        public string? NumeroNotaFiscal { get; set; }
        public Dinheiro ValorTotal { get; set; } = null!;
        public string? Observacoes { get; set; }
        public DateTime CriadoEm { get; set; }

        public Empresa? Empresa { get; set; }
        public Loja? Loja { get; set; }
        public ICollection<ItemVenda>? ItensVenda { get; set; }
        public ICollection<MovimentacaoEstoque>? Movimentacoes { get; set; }

        public static Venda Criar(
            Guid id,
            Guid empresaId,
            CanalVenda canal,
            NaturezaMovimentacaoEstoque natureza,
            DateTime dataVenda,
            DateTime? dataEnvio,
            string? numeroNotaFiscal,
            string? observacoes,
            DateTime criadoEm) =>
            new()
            {
                Id = id,
                EmpresaId = empresaId,
                Canal = canal,
                Natureza = natureza,
                DataVenda = dataVenda,
                DataEnvio = dataEnvio,
                NumeroNotaFiscal = string.IsNullOrWhiteSpace(numeroNotaFiscal) ? null : numeroNotaFiscal.Trim(),
                ValorTotal = Dinheiro.Zero,
                Observacoes = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes.Trim(),
                CriadoEm = criadoEm,
                ItensVenda = new List<ItemVenda>()
            };

        public void AdicionarItem(ItemVenda item)
        {
            ItensVenda ??= new List<ItemVenda>();
            ItensVenda.Add(item);
            RecalcularValorTotal();
        }

        public void RecalcularValorTotal()
        {
            var total = ItensVenda?.Aggregate(Dinheiro.Zero, (acc, item) => acc.Add(item.PrecoTotal)) ?? Dinheiro.Zero;
            ValorTotal = total;
        }
    }
}
