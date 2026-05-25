using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Notifications;

// ── Criar ─────────────────────────────────────────────────────────────────────

public sealed record CriarRotinaCommand(
    string Codigo,
    string Nome,
    TipoEventoNotificacao TipoEvento,
    TriggerTipoRotina TriggerTipo,
    string TemplateCodigo,
    CategoriaConteudoNotificacao CategoriaConteudo,
    string? CronExpression = null,
    string? ParametrosJson = null,
    Guid? EmpresaId = null) : ICommand;

public sealed record RotinaResult(Guid Id, string Codigo);

public sealed class CriarRotinaUseCase(
    IRotinaRepository rotinaRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarRotinaUseCase> logger)
    : IUseCase<CriarRotinaCommand, RotinaResult>
{
    public async Task<RotinaResult> ExecuteAsync(CriarRotinaCommand command)
    {
        var rotina = RotinaNotificacao.Criar(
            command.Codigo, command.Nome, command.TipoEvento,
            command.TriggerTipo, command.TemplateCodigo, command.CategoriaConteudo,
            command.CronExpression, command.EmpresaId);

        if (command.ParametrosJson is not null)
            rotina.DefinirParametros(command.ParametrosJson, "sistema");

        await rotinaRepository.AddAsync(rotina);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Rotina criada: {Codigo}", rotina.Codigo);
        return new RotinaResult(rotina.Id, rotina.Codigo);
    }
}

// ── Atualizar ─────────────────────────────────────────────────────────────────

public sealed record AtualizarRotinaCommand(
    Guid RotinaId,
    string? CronExpression,
    string? ParametrosJson,
    string AtualizadoPor) : ICommand;

public sealed class AtualizarRotinaUseCase(
    IRotinaRepository rotinaRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AtualizarRotinaCommand, RotinaResult>
{
    public async Task<RotinaResult> ExecuteAsync(AtualizarRotinaCommand command)
    {
        var rotina = await rotinaRepository.GetByIdAsync(command.RotinaId)
            ?? throw new InvalidOperationException($"Rotina {command.RotinaId} não encontrada.");

        if (command.CronExpression is not null)
            rotina.DefinirCronExpression(command.CronExpression, command.AtualizadoPor);

        if (command.ParametrosJson is not null)
            rotina.DefinirParametros(command.ParametrosJson, command.AtualizadoPor);

        await rotinaRepository.UpdateAsync(rotina);
        await unitOfWork.CommitAsync();
        return new RotinaResult(rotina.Id, rotina.Codigo);
    }
}

// ── Ativar / Desativar ────────────────────────────────────────────────────────

public sealed record AtivarRotinaCommand(Guid RotinaId, string AtualizadoPor) : ICommand;
public sealed record DesativarRotinaCommand(Guid RotinaId, string AtualizadoPor) : ICommand;
public sealed record AtivarRotinaResult(bool Ativa);

public sealed class AtivarRotinaUseCase(
    IRotinaRepository rotinaRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AtivarRotinaCommand, AtivarRotinaResult>
{
    public async Task<AtivarRotinaResult> ExecuteAsync(AtivarRotinaCommand command)
    {
        var rotina = await rotinaRepository.GetByIdAsync(command.RotinaId)
            ?? throw new InvalidOperationException($"Rotina {command.RotinaId} não encontrada.");

        rotina.Ativar(command.AtualizadoPor);
        await rotinaRepository.UpdateAsync(rotina);
        await unitOfWork.CommitAsync();
        return new AtivarRotinaResult(true);
    }
}

public sealed class DesativarRotinaUseCase(
    IRotinaRepository rotinaRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<DesativarRotinaCommand, AtivarRotinaResult>
{
    public async Task<AtivarRotinaResult> ExecuteAsync(DesativarRotinaCommand command)
    {
        var rotina = await rotinaRepository.GetByIdAsync(command.RotinaId)
            ?? throw new InvalidOperationException($"Rotina {command.RotinaId} não encontrada.");

        rotina.Desativar(command.AtualizadoPor);
        await rotinaRepository.UpdateAsync(rotina);
        await unitOfWork.CommitAsync();
        return new AtivarRotinaResult(false);
    }
}
