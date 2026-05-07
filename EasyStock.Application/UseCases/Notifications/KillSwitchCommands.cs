using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Notifications;

public sealed record AtivarKillSwitchCommand(
    string Motivo,
    string AtivadoPor,
    Guid? EmpresaId = null,
    CanalNotificacao? Canal = null,
    DateTime? ExpiraEm = null) : ICommand;

public sealed record RemoverKillSwitchCommand(Guid BloqueioId, string RemovidoPor) : ICommand;

public sealed record KillSwitchResult(Guid BloqueioId);

public sealed class AtivarKillSwitchUseCase(
    IBloqueioNotificacaoRepository bloqueioRepository,
    IUnitOfWork unitOfWork,
    ILogger<AtivarKillSwitchUseCase> logger)
    : IUseCase<AtivarKillSwitchCommand, KillSwitchResult>
{
    public async Task<KillSwitchResult> ExecuteAsync(AtivarKillSwitchCommand command)
    {
        var bloqueio = BloqueioNotificacao.Criar(
            command.Motivo, command.AtivadoPor,
            command.EmpresaId, command.Canal, command.ExpiraEm);

        await bloqueioRepository.AddAsync(bloqueio);
        await unitOfWork.CommitAsync();

        logger.LogWarning(
            "Kill switch ativado por {AtivadoPor}: motivo={Motivo} canal={Canal} empresa={EmpresaId}",
            command.AtivadoPor, command.Motivo, command.Canal, command.EmpresaId);

        return new KillSwitchResult(bloqueio.Id);
    }
}

public sealed class RemoverKillSwitchUseCase(
    IBloqueioNotificacaoRepository bloqueioRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<RemoverKillSwitchCommand, KillSwitchResult>
{
    public async Task<KillSwitchResult> ExecuteAsync(RemoverKillSwitchCommand command)
    {
        var bloqueio = await bloqueioRepository.GetByIdAsync(command.BloqueioId)
            ?? throw new InvalidOperationException($"Bloqueio {command.BloqueioId} não encontrado.");

        bloqueio.Remover(command.RemovidoPor);
        await bloqueioRepository.UpdateAsync(bloqueio);
        await unitOfWork.CommitAsync();

        return new KillSwitchResult(bloqueio.Id);
    }
}
