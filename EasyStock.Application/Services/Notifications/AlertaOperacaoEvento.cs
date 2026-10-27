using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Construtor único do <see cref="TipoEventoNotificacao.AlertaOperacao"/> (#2096): aviso da operação que não é prazo
/// estourado (encomenda chegando, encomenda sem confirmação, estoque para conferir, reclamação e suspeita vistas pela IA).
/// Vai para dona e gerente por Push, e-mail e sino.
///
/// <para>
/// O Push aparece na tela de bloqueio: leva só <c>titulo</c> e <c>resumo_curto</c>, sem contato do cliente (#1807). O
/// texto completo vai no e-mail e no sino. O <c>CorrelationId</c> é <c>av:{sigla}:{referencia:N}{sufixo}</c> (até 64
/// caracteres) e é a trava de uma vez só: o produtor consulta antes de enfileirar.
/// </para>
/// </summary>
public static class AlertaOperacaoEvento
{
    /// <summary>Chave do payload com o código do aviso; o interruptor da loja lê esta chave.</summary>
    public const string ChaveAviso = "aviso";

    public const int ResumoCurtoMaximo = 90;
    public const int TextoMaximo = 600;

    public static string Sigla(string codigo) => codigo switch
    {
        CodigosAviso.EncomendaProxima => "ep",
        CodigosAviso.EncomendaSemConfirmacao => "ec",
        CodigosAviso.EstoqueParaConferir => "es",
        CodigosAviso.ReclamacaoCliente => "rc",
        CodigosAviso.SuspeitaAtendimento => "sa",
        _ => throw new ArgumentOutOfRangeException(nameof(codigo), codigo, "Aviso sem sigla de correlação."),
    };

    /// <param name="sufixo">Distingue avisos da mesma referência (ex.: o horário da encomenda, o dia do resumo).</param>
    public static string CorrelationId(string codigo, Guid referenciaId, string? sufixo = null)
    {
        var id = $"av:{Sigla(codigo)}:{referenciaId:N}{(string.IsNullOrEmpty(sufixo) ? "" : ":" + sufixo)}";
        if (id.Length > 64) throw new ArgumentException("CorrelationId passa de 64 caracteres.", nameof(sufixo));
        return id;
    }

    public static string Payload(string codigo, string titulo, string resumoCurto, string texto, string link, string correlationId)
    {
        var dados = new Dictionary<string, object?>
        {
            [ChaveAviso] = codigo,
            ["titulo"] = Cortar(titulo, 60),
            ["resumo_curto"] = Cortar(resumoCurto, ResumoCurtoMaximo),
            ["texto"] = Cortar(texto, TextoMaximo),
            ["link"] = link,
            [NotificadorService.ChaveIdempotenciaPayload] = correlationId,
        };
        return JsonSerializer.Serialize(dados);
    }

    /// <summary>
    /// Enfileira na unidade de trabalho atual, sem commit. Devolve false quando o mesmo aviso já foi enfileirado (consulta
    /// pelo <c>CorrelationId</c>) ou quando o interruptor geral dos prazos está desligado.
    /// </summary>
    public static async Task<bool> EnfileirarAsync(
        INotificadorService notificador, IEventoNotificacaoRepository eventos, PrazosOptions opcoes,
        string codigo, Guid empresaId, Guid referenciaId, string titulo, string resumoCurto, string texto,
        CancellationToken ct, string? sufixo = null, string? link = null)
    {
        if (!opcoes.Habilitado) return false;
        var correlationId = CorrelationId(codigo, referenciaId, sufixo);
        if (await eventos.ExisteCorrelacaoAsync(empresaId, correlationId, ct)) return false;

        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.AlertaOperacao, empresaId,
            Payload(codigo, titulo, resumoCurto, texto, link ?? opcoes.LinkConsole(), correlationId),
            referenciaId, ct, correlationId);
        return true;
    }

    private static string Cortar(string? texto, int maximo)
    {
        var limpo = (texto ?? string.Empty).Trim();
        return limpo.Length <= maximo ? limpo : limpo[..(maximo - 1)].TrimEnd() + "…";
    }
}
