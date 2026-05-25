using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Notifications;

public sealed record PublicarEventoNotificacaoCommand(
    TipoEventoNotificacao TipoEvento,
    Guid EmpresaId,
    Guid? UsuarioDestinoId,
    string PayloadJson,
    IDictionary<string, object?>? VarsAdicionais = null) : ICommand;

public sealed record PublicarEventoNotificacaoResult(bool Publicado);

public sealed class PublicarEventoNotificacaoUseCase(
    INotificadorService notificadorService,
    ILogger<PublicarEventoNotificacaoUseCase> logger)
    : IUseCase<PublicarEventoNotificacaoCommand, PublicarEventoNotificacaoResult>
{
    public async Task<PublicarEventoNotificacaoResult> ExecuteAsync(
        PublicarEventoNotificacaoCommand command)
    {
        logger.LogDebug(
            "Publicando evento {TipoEvento} para empresa {EmpresaId}",
            command.TipoEvento, command.EmpresaId);

        await notificadorService.PublicarEventoAsync(
            command.TipoEvento,
            command.EmpresaId,
            command.UsuarioDestinoId,
            command.PayloadJson,
            command.VarsAdicionais);

        return new PublicarEventoNotificacaoResult(Publicado: true);
    }
}
