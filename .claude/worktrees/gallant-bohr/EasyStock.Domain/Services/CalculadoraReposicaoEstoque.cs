using System;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Services
{
    public sealed class CalculadoraReposicaoEstoque
    {
        // Calcula a quantidade sugerida de reposição considerando consumo médio diário,
        // tempo de reposição (lead time) e dias de estoque de segurança.
        // Retorna Quantidade.Zero se não for necessário repor.
        public Quantidade CalcularQuantidadeReposicao(
            Quantidade quantidadeAtual,
            int consumoMedioDiario,
            int tempoReposicaoDias,
            int estoqueSegurancaDias,
            int tamanhoLote = 1)
        {
            if (consumoMedioDiario < 0) throw new ArgumentOutOfRangeException(nameof(consumoMedioDiario));
            if (tempoReposicaoDias < 0) throw new ArgumentOutOfRangeException(nameof(tempoReposicaoDias));
            if (estoqueSegurancaDias < 0) throw new ArgumentOutOfRangeException(nameof(estoqueSegurancaDias));
            if (tamanhoLote <= 0) throw new ArgumentOutOfRangeException(nameof(tamanhoLote));

            var demandaLeadTime = consumoMedioDiario * tempoReposicaoDias;
            var estoqueSeguranca = consumoMedioDiario * estoqueSegurancaDias;
            var necessario = demandaLeadTime + estoqueSeguranca - quantidadeAtual.Value;

            if (necessario <= 0) return Quantidade.Zero;

            // Ajustar para o múltiplo do tamanho do lote (arredonda para cima)
            var unidades = ((necessario + tamanhoLote - 1) / tamanhoLote) * tamanhoLote;

            return Quantidade.From(unidades);
        }
    }
}
