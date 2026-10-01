using EasyStock.Application.Ports.Output.Persistence.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Prazo dos impressos do pedido (S49, S52), igual no Pedido e na Comanda. Agendado: janela da vaga ativa, senão
/// o horário agendado; pronto até = início − <see cref="MinutosProntoAntesDaJanela"/>. Imediato: pronto = último
/// pagamento (ou criação) + tempo de preparo da loja; saída = pronto + <see cref="MinutosProntoAntesDaJanela"/>.
/// </summary>
public static class PrazoImpresso
{
    /// <summary>Folga entre o pronto e a entrega. Fixa até a S50 torná-la configurável por loja.</summary>
    public const int MinutosProntoAntesDaJanela = 30;

    public static PedidoImpressoPrazoDto Calcular(PedidoImpressoLeitura p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var folga = TimeSpan.FromMinutes(MinutosProntoAntesDaJanela);
        if (p.Janela is { } j)
        {
            var inicio = j.Data.ToDateTime(j.Inicio);
            return new PedidoImpressoPrazoDto(true, inicio, j.Data.ToDateTime(j.Fim), inicio - folga);
        }
        if (p.AgendadoParaEm is { } agendado)
        {
            var inicio = HorarioBrasil.ConverterParaBrasilia(agendado);
            return new PedidoImpressoPrazoDto(true, inicio, null, inicio - folga);
        }
        var ultimoPagamentoEm = p.Pagamentos.Count == 0 ? (DateTime?)null : p.Pagamentos.Max(g => g.PagoEm);
        var pronto = HorarioBrasil.ConverterParaBrasilia(ultimoPagamentoEm ?? p.CriadoEm)
            .AddMinutes(p.TempoPreparoPadraoMinutos);
        return new PedidoImpressoPrazoDto(false, pronto + folga, null, pronto);
    }
}
