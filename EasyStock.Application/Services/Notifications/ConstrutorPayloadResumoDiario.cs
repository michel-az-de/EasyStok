using System.Globalization;
using System.Text.Json;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Payload do <see cref="TipoEventoNotificacao.ResumoDiario"/> (N12): o resumo do dia de Brasília da empresa (pedidos
/// entregues, faturamento, ticket médio, pendentes, caixa e Pix) já formatado em pt-BR, com as variáveis dos templates da N13.
/// Precisa rodar com o tenant da empresa ligado: fora dele o filtro do EF devolve zero sem erro.
/// </summary>
public sealed class ConstrutorPayloadResumoDiario(IAnalyticsRepository analytics) : IConstrutorPayloadAgendado
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public TipoEventoNotificacao Tipo => TipoEventoNotificacao.ResumoDiario;

    public async Task<string> ConstruirAsync(Guid empresaId, DateOnly diaLocal, CancellationToken ct = default)
    {
        var resumo = await analytics.GetResumoDiaAsync(empresaId, null);

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["data"] = diaLocal.ToString("dd/MM/yyyy", PtBr),
            ["entregues"] = resumo.PedidosEntreguesHoje,
            ["faturamento"] = Dinheiro(resumo.FaturamentoHoje),
            ["ticket_medio"] = Dinheiro(resumo.TicketMedioHoje),
            ["pendentes"] = resumo.PedidosPendentes,
            ["valor_pendentes"] = Dinheiro(resumo.ValorPedidosPendentes),
            ["caixa_texto"] = TextoDoCaixa(resumo),
            ["pix_texto"] = TextoDoPix(resumo),
        });
    }

    private static string Dinheiro(decimal valor) => "R$ " + valor.ToString("N2", PtBr);

    private static string TextoDoCaixa(ResumoDia r) =>
        r.CaixaAbertaHoje ? $"aberto com saldo de {Dinheiro(r.SaldoCaixaAtual)}"
        : r.CaixaFechadaHoje ? "fechado"
        : "sem movimento hoje";

    private static string TextoDoPix(ResumoDia r) => r.PixRecebidosHoje switch
    {
        <= 0 => "nenhum Pix recebido",
        1 => $"1 Pix recebido, somando {Dinheiro(r.ValorPixHoje)}",
        var n => $"{n} Pix recebidos, somando {Dinheiro(r.ValorPixHoje)}",
    };
}
