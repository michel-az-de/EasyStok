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
        public ICollection<ItemVenda> ItensVenda { get; set; } = new List<ItemVenda>();
        public ICollection<MovimentacaoEstoque> Movimentacoes { get; set; } = new List<MovimentacaoEstoque>();

        public static Venda Criar(
            Guid id,
            Guid empresaId,
            CanalVenda canal,
            NaturezaMovimentacaoEstoque natureza,
            DateTime dataVenda,
            DateTime? dataEnvio,
            string? numeroNotaFiscal,
            string? observacoes,
            DateTime criadoEm)
        {
            if (id == Guid.Empty) throw new ArgumentException("Id da venda é obrigatório.", nameof(id));
            if (empresaId == Guid.Empty) throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
            if (dataVenda == default) throw new ArgumentException("DataVenda é obrigatória.", nameof(dataVenda));
            if (dataEnvio.HasValue && dataEnvio.Value < dataVenda)
                throw new ArgumentException("DataEnvio nao pode ser anterior a DataVenda.", nameof(dataEnvio));
            if (criadoEm == default) criadoEm = DateTime.UtcNow;

            return new Venda
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
        }

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
