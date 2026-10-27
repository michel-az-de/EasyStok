using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Interruptor da loja para os avisos da operação (#2096). Fica na frente do <see cref="NotificadorService"/>: evento
/// <see cref="TipoEventoNotificacao.PrazoEstourado"/> ou <see cref="TipoEventoNotificacao.AlertaOperacao"/> com a chave
/// <c>aviso</c> no payload só é enfileirado se a dona deixou o aviso ligado na tela Avisos. Assim os produtores (fila,
/// atraso, impressão, caixa, vigia, IA) não precisam saber da preferência. Sem a chave, nada muda (disparo de teste,
/// produtor antigo).
///
/// <para>
/// O repositório da configuração é resolvido só quando um desses eventos chega: o Worker usa este serviço para avaliar
/// eventos e não precisa registrar o atendimento (#2096, lição da dependência nova em use case).
/// </para>
/// </summary>
public sealed class NotificadorComAvisosDaLoja(
    INotificadorService interno,
    IServiceProvider servicos,
    ILogger<NotificadorComAvisosDaLoja> logger) : INotificadorService
{
    public Task PublicarEventoAsync(
        TipoEventoNotificacao tipo, Guid empresaId, Guid? usuarioDestinoId, string payloadJson,
        IDictionary<string, object?>? varsAdicionais = null, CancellationToken ct = default) =>
        interno.PublicarEventoAsync(tipo, empresaId, usuarioDestinoId, payloadJson, varsAdicionais, ct);

    public async Task<Guid> EnfileirarEventoAsync(
        TipoEventoNotificacao tipo, Guid empresaId, string payloadJson, Guid? refEntidadeId = null,
        CancellationToken ct = default, string? correlationId = null)
    {
        if (tipo is TipoEventoNotificacao.PrazoEstourado or TipoEventoNotificacao.AlertaOperacao
            && LerAviso(payloadJson) is { } aviso
            && !await LigadoAsync(empresaId, aviso))
        {
            logger.LogInformation("Aviso {Aviso} desligado pela loja {EmpresaId}: não enfileirado", aviso, empresaId);
            return Guid.Empty;
        }

        return await interno.EnfileirarEventoAsync(tipo, empresaId, payloadJson, refEntidadeId, ct, correlationId);
    }

    public Task AvaliarEventoAsync(EventoNotificacao evento, CancellationToken ct = default) =>
        interno.AvaliarEventoAsync(evento, ct);

    private async Task<bool> LigadoAsync(Guid empresaId, string aviso)
    {
        var repositorio = servicos.GetService<IConfiguracaoAtendimentoRepository>();
        if (repositorio is null) return true;
        var configuracao = await repositorio.GetOrDefaultAsync(empresaId);
        return configuracao.Avisos.Ligado(aviso);
    }

    internal static string? LerAviso(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty(AlertaOperacaoEvento.ChaveAviso, out var v)
                   && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
