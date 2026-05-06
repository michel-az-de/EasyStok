using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Notifications;

/// <summary>
/// Broadcast ad-hoc para super-admin. Cria um EventoNotificacao do tipo BroadcastSuperAdmin
/// para cada usuário destino e enfileira no outbox.
/// </summary>
public sealed record EnviarNotificacaoManualCommand(
    Guid EmpresaId,
    IReadOnlyList<Guid> UsuariosDestinoIds,
    string Titulo,
    string Mensagem,
    string EnviadoPor,
    CanalNotificacao? CanalForcar = null) : ICommand;

public sealed record EnviarNotificacaoManualResult(int TotalEnfileirado);

public sealed class EnviarNotificacaoManualUseCase(
    INotificadorService notificadorService,
    ILogger<EnviarNotificacaoManualUseCase> logger)
    : IUseCase<EnviarNotificacaoManualCommand, EnviarNotificacaoManualResult>
{
    public async Task<EnviarNotificacaoManualResult> ExecuteAsync(EnviarNotificacaoManualCommand command)
    {
        var enfileirados = 0;

        foreach (var usuarioId in command.UsuariosDestinoIds)
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                usuarioId = usuarioId.ToString(),
                titulo = command.Titulo,
                mensagem = command.Mensagem,
                enviadoPor = command.EnviadoPor
            });

            await notificadorService.PublicarEventoAsync(
                TipoEventoNotificacao.BroadcastSuperAdmin,
                command.EmpresaId,
                usuarioId,
                payload);

            enfileirados++;
        }

        logger.LogInformation(
            "Broadcast manual enviado por {EnviadoPor}: {Total} destinatários empresa={EmpresaId}",
            command.EnviadoPor, enfileirados, command.EmpresaId);

        return new EnviarNotificacaoManualResult(enfileirados);
    }
}
