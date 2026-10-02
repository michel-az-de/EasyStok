using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

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
    Guid? EmpresaId = null,
    IReadOnlyList<CanalNotificacao>? Canais = null) : ICommand;

public sealed record RotinaResult(Guid Id, string Codigo);

/// <summary>Regras de <c>ParametrosJson</c> que a API impõe à rotina da empresa (N4).</summary>
internal static class ParametrosDaRotina
{
    /// <summary>
    /// O Admin de uma empresa também grava <c>ParametrosJson</c>: apontar a rotina dele para os superadmins mandaria o
    /// aviso (e o contato deles) para fora do tenant. A audiência <c>superadmins</c> só vale em rotina global.
    /// </summary>
    public static void ValidarAudiencia(string? parametrosJson, Guid? empresaId)
    {
        if (empresaId is not null && AudienciaDaRotina.Ler(parametrosJson) == AudienciaNotificacao.Superadmins)
            throw new UseCaseValidationException(
                "AUDIENCIA_SUPERADMINS_SO_GLOBAL",
                "A audiência 'superadmins' só vale em rotina global, não em rotina da empresa.");
    }
}

/// <summary>Regras da agenda diária (N12): <c>agenda.horario</c> em HH:mm de Brasília; cron não é suportado.</summary>
internal static class AgendaDaRotina
{
    public static void ValidarCron(TriggerTipoRotina? trigger, string? cronExpression)
    {
        if (trigger == TriggerTipoRotina.Cron || !string.IsNullOrWhiteSpace(cronExpression))
            throw new UseCaseValidationException(
                "CRON_NAO_SUPORTADO",
                "Cron não é suportado: use agenda.horario (HH:mm, horário de Brasília) em ParametrosJson.");
    }

    public static void ValidarHorario(string? parametrosJson)
    {
        if (!AgendaDiariaLocal.TemAgenda(parametrosJson)) return;
        if (AgendaDiariaLocal.HorarioDosParametros(parametrosJson) is null)
            throw new UseCaseValidationException(
                "AGENDA_HORARIO_INVALIDO",
                "agenda.horario deve estar no formato HH:mm (00:00 a 23:59), em horário de Brasília.");
    }
}

public sealed class CriarRotinaUseCase(
    IRotinaRepository rotinaRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarRotinaUseCase> logger)
    : IUseCase<CriarRotinaCommand, RotinaResult>
{
    public async Task<RotinaResult> ExecuteAsync(CriarRotinaCommand command)
    {
        ParametrosDaRotina.ValidarAudiencia(command.ParametrosJson, command.EmpresaId);
        AgendaDaRotina.ValidarCron(command.TriggerTipo, command.CronExpression);
        AgendaDaRotina.ValidarHorario(command.ParametrosJson);

        var rotina = RotinaNotificacao.Criar(
            command.Codigo, command.Nome, command.TipoEvento,
            command.TriggerTipo, command.TemplateCodigo, command.CategoriaConteudo,
            command.CronExpression, command.EmpresaId);

        if (command.ParametrosJson is not null)
            rotina.DefinirParametros(command.ParametrosJson, "sistema");

        // N5: canais em ordem de preferência, sem repetição. Sem eles a rotina nasce sem canais, como antes.
        if (command.Canais is { Count: > 0 })
            rotina.DefinirFallback(CanaisDaRotina.Serializar(command.Canais.Distinct()), "sistema");

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
    string AtualizadoPor,
    Guid EmpresaId) : ICommand;

public sealed class AtualizarRotinaUseCase(
    IRotinaRepository rotinaRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AtualizarRotinaCommand, RotinaResult>
{
    public async Task<RotinaResult> ExecuteAsync(AtualizarRotinaCommand command)
    {
        var rotina = await rotinaRepository.ObterDaEmpresaAsync(command.RotinaId, command.EmpresaId);

        ParametrosDaRotina.ValidarAudiencia(command.ParametrosJson, rotina.EmpresaId);
        AgendaDaRotina.ValidarCron(null, command.CronExpression);
        AgendaDaRotina.ValidarHorario(command.ParametrosJson);

        if (command.ParametrosJson is not null)
            rotina.DefinirParametros(command.ParametrosJson, command.AtualizadoPor);

        await rotinaRepository.UpdateAsync(rotina);
        await unitOfWork.CommitAsync();
        return new RotinaResult(rotina.Id, rotina.Codigo);
    }
}

// ── Ativar / Desativar ────────────────────────────────────────────────────────

public sealed record AtivarRotinaCommand(Guid RotinaId, string AtualizadoPor, Guid EmpresaId) : ICommand;
public sealed record DesativarRotinaCommand(Guid RotinaId, string AtualizadoPor, Guid EmpresaId) : ICommand;
public sealed record AtivarRotinaResult(bool Ativa);

public sealed class AtivarRotinaUseCase(
    IRotinaRepository rotinaRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<AtivarRotinaCommand, AtivarRotinaResult>
{
    public async Task<AtivarRotinaResult> ExecuteAsync(AtivarRotinaCommand command)
    {
        var rotina = await rotinaRepository.ObterDaEmpresaAsync(command.RotinaId, command.EmpresaId);

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
        var rotina = await rotinaRepository.ObterDaEmpresaAsync(command.RotinaId, command.EmpresaId);

        rotina.Desativar(command.AtualizadoPor);
        await rotinaRepository.UpdateAsync(rotina);
        await unitOfWork.CommitAsync();
        return new AtivarRotinaResult(false);
    }
}
