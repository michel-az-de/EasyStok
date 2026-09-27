using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Expediente;

public sealed record ExpedienteLojaResult(
    Guid EmpresaId,
    IReadOnlyList<HorarioFuncionamento> Horarios,
    ControleManualLoja ControleManual,
    Guid? ControleAlteradoPorUsuarioId,
    DateTime? ControleAlteradoEm,
    string MensagemForaDoHorario,
    string MensagemLojaFechada,
    bool EstaAberta,
    DateTime? ProximaAberturaLocal)
{
    internal static ExpedienteLojaResult De(ExpedienteLoja e, DateTime agoraUtc) => new(
        e.EmpresaId, e.Horarios, e.ControleManual, e.ControleAlteradoPorUsuarioId, e.ControleAlteradoEm,
        e.MensagemForaDoHorario, e.MensagemLojaFechada, e.EstaAberta(agoraUtc), e.ProximaAberturaLocal(agoraUtc));
}

/// <summary>S40: devolve o expediente; sem registro, o padrão em memória (nunca 404).</summary>
public sealed class ObterExpedienteLojaUseCase(IExpedienteLojaRepository repository, TimeProvider relogio)
{
    public async Task<ExpedienteLojaResult> ExecuteAsync(Guid empresaId, CancellationToken ct = default)
    {
        var expediente = await repository.GetByEmpresaIdAsync(empresaId, ct) ?? ExpedienteLoja.CriarPadrao(empresaId);
        return ExpedienteLojaResult.De(expediente, relogio.GetUtcNow().UtcDateTime);
    }
}

public sealed record AtualizarExpedienteLojaCommand(
    Guid EmpresaId,
    IReadOnlyList<HorarioFuncionamento>? Horarios,
    string? MensagemForaDoHorario,
    string? MensagemLojaFechada);

/// <summary>S40: grava horários e mensagens (<c>EDITAR_FUNCIONAMENTO</c> do protótipo).</summary>
public sealed class AtualizarExpedienteLojaUseCase(
    IExpedienteLojaRepository repository, IUnitOfWork unitOfWork, IOperacaoEventPublisher publisher, TimeProvider relogio)
{
    public async Task<ExpedienteLojaResult> ExecuteAsync(AtualizarExpedienteLojaCommand command, CancellationToken ct = default)
    {
        var (expediente, novo) = await CarregarAsync(repository, command.EmpresaId, ct);

        try
        {
            if (command.Horarios is not null) expediente.DefinirHorarios(command.Horarios);
            expediente.DefinirMensagens(command.MensagemForaDoHorario, command.MensagemLojaFechada);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        return await GravarAsync(repository, unitOfWork, publisher, expediente, novo, relogio, ct);
    }

    internal static async Task<(ExpedienteLoja Expediente, bool Novo)> CarregarAsync(
        IExpedienteLojaRepository repository, Guid empresaId, CancellationToken ct)
    {
        var existente = await repository.GetByEmpresaIdAsync(empresaId, ct);
        return existente is null ? (ExpedienteLoja.CriarPadrao(empresaId), true) : (existente, false);
    }

    internal static async Task<ExpedienteLojaResult> GravarAsync(
        IExpedienteLojaRepository repository, IUnitOfWork unitOfWork, IOperacaoEventPublisher publisher,
        ExpedienteLoja expediente, bool novo, TimeProvider relogio, CancellationToken ct)
    {
        if (novo) await repository.AddAsync(expediente, ct);
        else await repository.UpdateAsync(expediente, ct);
        await unitOfWork.CommitAsync();

        var resultado = ExpedienteLojaResult.De(expediente, relogio.GetUtcNow().UtcDateTime);
        // Depois do commit: o console atualiza o trilho de "loja aberta" em tempo real (SSE, S18).
        await publisher.PublicarAsync("expediente.alterado", expediente.EmpresaId,
            new { estaAberta = resultado.EstaAberta, controleManual = resultado.ControleManual }, ct);
        return resultado;
    }
}

public sealed record DefinirControleExpedienteCommand(Guid EmpresaId, ControleManualLoja Controle, Guid? UsuarioId);

/// <summary>S40: abre ou fecha na mão, ou volta ao automático (<c>ALTERNAR_LOJA</c> do protótipo).</summary>
public sealed class DefinirControleExpedienteUseCase(
    IExpedienteLojaRepository repository, IUnitOfWork unitOfWork, IOperacaoEventPublisher publisher, TimeProvider relogio)
{
    public async Task<ExpedienteLojaResult> ExecuteAsync(DefinirControleExpedienteCommand command, CancellationToken ct = default)
    {
        var (expediente, novo) = await AtualizarExpedienteLojaUseCase.CarregarAsync(repository, command.EmpresaId, ct);

        try
        {
            expediente.DefinirControle(command.Controle, command.UsuarioId, relogio.GetUtcNow().UtcDateTime);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        return await AtualizarExpedienteLojaUseCase.GravarAsync(repository, unitOfWork, publisher, expediente, novo, relogio, ct);
    }
}
