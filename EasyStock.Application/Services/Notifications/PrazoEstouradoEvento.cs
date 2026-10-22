using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>Os quatro prazos vivos do <see cref="TipoEventoNotificacao.PrazoEstourado"/> (N11).</summary>
public enum TipoPrazo
{
    ClienteSemResposta,
    PedidoAtrasado,
    ImpressaoTravada,
    CaixaEsquecido,
}

/// <summary>
/// Construtor único do evento <c>PrazoEstourado</c> (N11): payload sem contato do cliente (só o nome, no cliente sem
/// resposta, #1765-8), link do console, chave de negócio
/// <c>prazo:{tipo}:{id}</c> e <c>CorrelationId</c> determinístico <c>prazo:{tipo}:{id:N}</c> (no máximo 59
/// caracteres, cabe no <c>varchar(64)</c>). Os quatro adaptadores enfileiram por aqui na unidade de trabalho do
/// próprio fato (ADR-0030), respeitando o interruptor <c>Notifications:Prazos:Habilitado</c>.
/// </summary>
public static class PrazoEstouradoEvento
{
    public static string Nome(TipoPrazo tipo) => tipo switch
    {
        TipoPrazo.ClienteSemResposta => "cliente_sem_resposta",
        TipoPrazo.PedidoAtrasado => "pedido_atrasado",
        TipoPrazo.ImpressaoTravada => "impressao_travada",
        TipoPrazo.CaixaEsquecido => "caixa_esquecido",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null),
    };

    public static string Legivel(TipoPrazo tipo) => tipo switch
    {
        TipoPrazo.ClienteSemResposta => "Cliente sem resposta",
        TipoPrazo.PedidoAtrasado => "Pedido sem início de preparo",
        TipoPrazo.ImpressaoTravada => "Impressão travada",
        TipoPrazo.CaixaEsquecido => "Caixa aberto de ontem",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null),
    };

    public static string ChaveIdempotencia(TipoPrazo tipo, Guid referenciaId) => $"prazo:{Nome(tipo)}:{referenciaId}";

    public static string CorrelationId(TipoPrazo tipo, Guid referenciaId) => $"prazo:{Nome(tipo)}:{referenciaId:N}";

    /// <summary>Código curto de 8 caracteres exibido ao operador.</summary>
    public static string Referencia(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    /// <summary>Duração em pt-BR: "35 minutos", "1 hora e 5 minutos", "2 dias".</summary>
    public static string Duracao(TimeSpan d)
    {
        var minutos = (int)Math.Max(1, Math.Round(d.TotalMinutes));
        if (minutos < 60) return minutos == 1 ? "1 minuto" : $"{minutos} minutos";
        if (minutos < 24 * 60)
        {
            var h = minutos / 60;
            var m = minutos % 60;
            var horas = h == 1 ? "1 hora" : $"{h} horas";
            return m == 0 ? horas : $"{horas} e {(m == 1 ? "1 minuto" : $"{m} minutos")}";
        }

        var dias = minutos / (24 * 60);
        return dias == 1 ? "1 dia" : $"{dias} dias";
    }

    /// <summary>Instante UTC em hora de parede de Brasília, "dd/MM/yyyy HH:mm".</summary>
    public static string InstanteTexto(DateTime utc) =>
        HorarioBrasil.ConverterParaBrasilia(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <param name="cliente">#1765-8: nome do cliente que espera, para a equipe saber de quem é (só no cliente sem resposta).</param>
    /// <param name="link">Onde resolver no console (<see cref="PrazosOptions.LinkPara"/>).</param>
    /// <param name="fatoId">De quem é a chave de idempotência; sem ele, <paramref name="referenciaId"/>. O cliente sem resposta
    /// usa a conversa: o aviso sai uma vez por conversa, não uma por mensagem do cliente.</param>
    public static string Payload(
        TipoPrazo tipo, Guid referenciaId, string referenciaCurta, string prazoTexto, string atrasoTexto, Guid? usuarioId = null,
        string? cliente = null, string? link = null, Guid? fatoId = null)
    {
        var dados = new Dictionary<string, object?>
        {
            ["tipo_legivel"] = Legivel(tipo),
            ["referencia"] = referenciaCurta,
            ["prazo_texto"] = prazoTexto,
            ["atraso_texto"] = atrasoTexto,
            ["link"] = link ?? string.Empty,
            [NotificadorService.ChaveIdempotenciaPayload] = ChaveIdempotencia(tipo, fatoId ?? referenciaId),
        };
        if (!string.IsNullOrWhiteSpace(cliente)) dados["cliente"] = cliente.Trim();
        if (usuarioId is { } u) dados["usuarioId"] = u;
        return JsonSerializer.Serialize(dados);
    }

    /// <summary>
    /// Enfileira o <c>PrazoEstourado</c> na unidade de trabalho atual (sem commit). Devolve false quando o
    /// interruptor está desligado. O <c>CorrelationId</c> segue <paramref name="referenciaId"/> (único por empresa); a chave
    /// de negócio, <paramref name="fatoId"/>, decide se a mensagem já saiu.
    /// </summary>
    public static async Task<bool> EnfileirarAsync(
        INotificadorService notificador, PrazosOptions opcoes,
        TipoPrazo tipo, Guid empresaId, Guid referenciaId, string referenciaCurta,
        string prazoTexto, string atrasoTexto, Guid? usuarioId, CancellationToken ct,
        string? cliente = null, Guid? fatoId = null)
    {
        if (!opcoes.Habilitado) return false;
        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.PrazoEstourado, empresaId,
            Payload(tipo, referenciaId, referenciaCurta, prazoTexto, atrasoTexto, usuarioId, cliente, opcoes.LinkPara(tipo), fatoId),
            referenciaId, ct, CorrelationId(tipo, referenciaId));
        return true;
    }
}
