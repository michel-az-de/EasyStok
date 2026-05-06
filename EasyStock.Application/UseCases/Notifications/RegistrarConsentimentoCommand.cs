using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Notifications;

public sealed record RegistrarOptInCommand(
    Guid UsuarioId,
    CanalNotificacao Canal,
    CategoriaConteudoNotificacao Categoria,
    string AtualizadoPor) : ICommand;

public sealed record RegistrarOptOutCommand(
    Guid UsuarioId,
    CanalNotificacao Canal,
    CategoriaConteudoNotificacao Categoria,
    string AtualizadoPor,
    string? Motivo = null) : ICommand;

public sealed record RegistrarConsentimentoResult(bool Registrado);

public sealed class RegistrarOptInUseCase(
    IConsentimentoRepository consentimentoRepository,
    IUnitOfWork unitOfWork,
    ILogger<RegistrarOptInUseCase> logger)
    : IUseCase<RegistrarOptInCommand, RegistrarConsentimentoResult>
{
    public async Task<RegistrarConsentimentoResult> ExecuteAsync(RegistrarOptInCommand command)
    {
        await UpsertConsentimento(command.UsuarioId, command.Canal, command.Categoria,
            optIn: true, atualizadoPor: command.AtualizadoPor, motivo: null);
        return new RegistrarConsentimentoResult(true);
    }

    private async Task UpsertConsentimento(Guid usuarioId, CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria, bool optIn, string atualizadoPor, string? motivo)
    {
        var existente = await consentimentoRepository.GetAsync(usuarioId, canal, categoria);

        if (existente is null)
        {
            var novo = ConsentimentoNotificacao.Registrar(
                usuarioId, canal, categoria, optIn, atualizadoPor, motivo);
            await consentimentoRepository.AddAsync(novo);
        }
        else
        {
            var atualizado = ConsentimentoNotificacao.Registrar(
                usuarioId, canal, categoria, optIn, atualizadoPor, motivo);
            await consentimentoRepository.AddAsync(atualizado); // histórico imutável
        }

        await unitOfWork.CommitAsync();
        logger.LogInformation(
            "Consentimento {OptIn} registrado para usuario={UsuarioId} canal={Canal} categoria={Categoria}",
            optIn, usuarioId, canal, categoria);
    }
}

public sealed class RegistrarOptOutUseCase(
    IConsentimentoRepository consentimentoRepository,
    IUnitOfWork unitOfWork,
    ILogger<RegistrarOptOutUseCase> logger)
    : IUseCase<RegistrarOptOutCommand, RegistrarConsentimentoResult>
{
    public async Task<RegistrarConsentimentoResult> ExecuteAsync(RegistrarOptOutCommand command)
    {
        var novo = ConsentimentoNotificacao.Registrar(
            command.UsuarioId, command.Canal, command.Categoria,
            optIn: false, atualizadoPor: command.AtualizadoPor,
            motivoOptOut: command.Motivo);
        await consentimentoRepository.AddAsync(novo);
        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Opt-out registrado para usuario={UsuarioId} canal={Canal}", command.UsuarioId, command.Canal);
        return new RegistrarConsentimentoResult(true);
    }
}
