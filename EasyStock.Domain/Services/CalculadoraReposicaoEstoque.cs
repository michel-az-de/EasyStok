using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Services
{
    /// <summary>
    /// Calcula a quantidade sugerida de reposição com base no consumo médio diário,
    /// tempo de reposição (lead time) e dias de estoque de segurança.
    /// </summary>
    public sealed class CalculadoraReposicaoEstoque
    {
        /// <summary>
        /// Retorna a quantidade a repor arredondada para o múltiplo do tamanho do lote.
        /// Retorna <see cref="Quantidade.Zero"/> quando o estoque atual já é suficiente.
        /// </summary>
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

            // Arredonda para cima até o próximo múltiplo do tamanho do lote
            var unidades = ((necessario + tamanhoLote - 1) / tamanhoLote) * tamanhoLote;

            return Quantidade.From(unidades);
        }
    }
}
