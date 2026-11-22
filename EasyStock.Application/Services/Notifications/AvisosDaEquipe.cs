using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Services.Notifications;

/// <summary>Um aviso da equipe pelo WhatsApp da loja (#2211): o código do interruptor (<see cref="CodigosAviso"/>), o tipo, a rotina e o template do molde global.</summary>
public sealed record AvisoDaEquipe(string Chave, string Nome, TipoEventoNotificacao Tipo, string RotinaCodigo, string TemplateCodigo);

/// <summary>
/// Catálogo dos avisos da equipe (#2211). Uma fonte só para o seed, a tela de configuração, a audiência e a proteção
/// contra laço do agente. Aviso novo entra aqui, no enum e no seed; o motor não muda.
/// </summary>
public static class AvisosDaEquipe
{
    public static readonly AvisoDaEquipe AtencaoNoAtendimento = new(
        CodigosAviso.EquipeAtencao, "Atendimento precisa de atenção", TipoEventoNotificacao.AvisoEquipeAtencaoAtendimento,
        "aviso_equipe_atencao_atendimento_global", "aviso_equipe_atencao_atendimento_whatsapp_v1");

    public static readonly AvisoDaEquipe PedidoComCanhoto = new(
        CodigosAviso.EquipePedido, "Pedido com o canhoto", TipoEventoNotificacao.AvisoEquipePedidoComCanhoto,
        "aviso_equipe_pedido_canhoto_global", "aviso_equipe_pedido_canhoto_whatsapp_v1");

    public static readonly AvisoDaEquipe ProblemaNoPagamento = new(
        CodigosAviso.EquipePagamento, "Problema no pagamento", TipoEventoNotificacao.AvisoEquipeProblemaPagamento,
        "aviso_equipe_problema_pagamento_global", "aviso_equipe_problema_pagamento_whatsapp_v1");

    public static readonly IReadOnlyList<AvisoDaEquipe> Todos = [AtencaoNoAtendimento, PedidoComCanhoto, ProblemaNoPagamento];

    public static IReadOnlyList<string> CodigosDeRotina { get; } = Todos.Select(a => a.RotinaCodigo).ToList();
}

/// <summary>
/// Publica os avisos da equipe (#2211). O evento entra no outbox na unidade de trabalho de quem chama (ADR-0030): sem
/// rotina ligada na empresa o avaliador descarta, então o produtor não precisa saber se a loja quer o aviso.
/// </summary>
public sealed class PublicadorAvisoEquipe(INotificadorService notificador)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public Task AtencaoNoAtendimentoAsync(
        Guid empresaId, Guid conversaId, string? cliente, string motivo, CancellationToken ct = default) =>
        EnfileirarAsync(AvisosDaEquipe.AtencaoNoAtendimento, empresaId, conversaId, new Dictionary<string, string?>
        {
            ["conversaId"] = conversaId.ToString(),
            ["cliente"] = cliente,
            ["motivo"] = SemPontoFinal(motivo),
        }, chave: null, ct);

    /// <param name="aReceber">
    /// #3002: quanto falta receber do pedido que entrou na fila sem pagar. Sem valor, a chave não vai no payload e o
    /// template diz "pago"; nula viraria texto vazio, que o Scriban lê como verdadeiro.
    /// </param>
    /// <param name="pagoDepois">
    /// #3168: o pedido já tinha canhoto (entrou na fila a receber) e foi quitado agora. Chave própria, porque a do
    /// canhoto já foi gasta pelo aviso "a receber" e o outbox descartaria este como repetido.
    /// </param>
    public Task PedidoComCanhotoAsync(
        Guid empresaId, Guid pedidoId, string? cliente, decimal total, CancellationToken ct = default, decimal aReceber = 0m,
        bool pagoDepois = false)
    {
        var payload = new Dictionary<string, string?>
        {
            ["pedidoId"] = pedidoId.ToString(),
            ["numero"] = CodigoCurto.De(pedidoId),
            ["cliente"] = cliente,
            ["total"] = total.ToString("C", PtBr),
        };
        if (aReceber > 0m) payload["aReceber"] = aReceber.ToString("C", PtBr);
        return EnfileirarAsync(AvisosDaEquipe.PedidoComCanhoto, empresaId, pedidoId, payload,
            chave: $"aviso-equipe-canhoto{(pagoDepois ? "-pago" : "")}:{pedidoId:N}", ct);
    }

    /// <param name="chave">Identidade do problema (pagamento e desfecho): webhook repetido não repete o aviso.</param>
    public Task ProblemaNoPagamentoAsync(
        Guid empresaId, Guid pedidoId, string? cliente, string problema, string chave, CancellationToken ct = default) =>
        EnfileirarAsync(AvisosDaEquipe.ProblemaNoPagamento, empresaId, pedidoId, new Dictionary<string, string?>
        {
            ["pedidoId"] = pedidoId.ToString(),
            ["numero"] = CodigoCurto.De(pedidoId),
            ["cliente"] = cliente,
            ["problema"] = SemPontoFinal(problema),
        }, chave: $"aviso-equipe-pagamento:{chave}", ct);

    /// <summary>#2917: o template já fecha a frase com ponto; texto livre com ponto final sairia com "..".</summary>
    private static string SemPontoFinal(string texto) => texto.Trim().TrimEnd('.').TrimEnd();

    private async Task EnfileirarAsync(
        AvisoDaEquipe aviso, Guid empresaId, Guid entidadeId, Dictionary<string, string?> payload, string? chave,
        CancellationToken ct)
    {
        payload[AlertaOperacaoEvento.ChaveAviso] = aviso.Chave;
        if (chave is not null) payload[NotificadorService.ChaveIdempotenciaPayload] = chave;
        await notificador.EnfileirarEventoAsync(aviso.Tipo, empresaId, JsonSerializer.Serialize(payload), entidadeId, ct, chave);
    }
}
