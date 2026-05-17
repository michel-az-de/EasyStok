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

        // --- Colunas de relatório (PR-B) --- adicionadas para suportar relatório
        // vendas.por-periodo. Nullable: vendas legadas anteriores ao PR-B ficam NULL.

        /// <summary>FK opcional para o usuário que realizou a venda (vendedor).</summary>
        public Guid? VendedorId { get; set; }

        /// <summary>Snapshot da forma de pagamento principal no fechamento da venda.
        /// Valores: "pix" | "dinheiro" | "credito" | "debito" | "transferencia" | "outro".</summary>
        public string? FormaPagamentoPrincipal { get; set; }

        /// <summary>Soma de (Quantidade × PrecoUnitario) de todos os itens antes de descontos.
        /// Para vendas sem desconto: Subtotal == ValorTotal. Snapshot imutável após criação.</summary>
        public Dinheiro? Subtotal { get; set; }

        /// <summary>Valor total de descontos aplicados (cupons, negociação, etc.).
        /// Inicialmente 0; atualizado quando CupomUso for integrado.</summary>
        public Dinheiro? ValorDesconto { get; set; }

        // --- Navegações ---
        public Empresa? Empresa { get; set; }
        public Loja? Loja { get; set; }
        public Usuario? Vendedor { get; set; }
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
            DateTime criadoEm,
            Guid? vendedorId = null,
            string? formaPagamentoPrincipal = null)
        {
            if (id == Guid.Empty) throw new ArgumentException("Id da venda é obrigatório.", nameof(id));
            if (empresaId == Guid.Empty) throw new ArgumentException("EmpresaId é obrigatório.", nameof(empresaId));
            if (dataVenda == default) throw new ArgumentException("DataVenda é obrigatória.", nameof(dataVenda));
            if (dataEnvio.HasValue && dataEnvio.Value < dataVenda)
                throw new ArgumentException("DataEnvio nao pode ser anterior a DataVenda.", nameof(dataEnvio));
            if (criadoEm == default) criadoEm = DateTime.UtcNow;

            var formasValidas = new[] { "pix", "dinheiro", "credito", "debito", "transferencia", "outro" };
            var forma = string.IsNullOrWhiteSpace(formaPagamentoPrincipal)
                ? null
                : formaPagamentoPrincipal.Trim().ToLowerInvariant();
            if (forma is not null && Array.IndexOf(formasValidas, forma) < 0)
                throw new ArgumentException($"FormaPagamentoPrincipal inválida: '{forma}'.", nameof(formaPagamentoPrincipal));

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
                Subtotal = Dinheiro.Zero,
                ValorDesconto = Dinheiro.Zero,
                Observacoes = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes.Trim(),
                CriadoEm = criadoEm,
                VendedorId = vendedorId,
                FormaPagamentoPrincipal = forma,
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
            // Subtotal = soma bruta dos itens (antes de descontos).
            // Para vendas sem desconto aplicado, Subtotal == ValorTotal.
            Subtotal = total.Add(ValorDesconto ?? Dinheiro.Zero);
        }
    }
}
