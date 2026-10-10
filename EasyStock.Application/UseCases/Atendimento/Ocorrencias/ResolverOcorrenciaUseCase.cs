using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

/// <param name="Valor">Valor do reembolso; sem valor, usa a operação em retomada ou o saldo disponível online.</param>
public sealed record ResolverOcorrenciaInput(
    Guid EmpresaId, Guid OcorrenciaId, Guid UsuarioId, string Resolucao, bool Reembolsar, decimal? Valor, NivelAcesso NivelSolicitante = NivelAcesso.Operador, string? UsuarioNome = null);

/// <param name="Reembolso">Null quando a resolução não pediu reembolso.</param>
public sealed record ResolverOcorrenciaResult(OcorrenciaDto Ocorrencia, ReembolsoResultado? Reembolso);

/// <summary>
/// Resolução humana da ocorrência (S27, RN-35), só pela dona no console. Sem reembolso grava a
/// resolução e não toca o gateway. Com reembolso chama <see cref="ReembolsarPedidoUseCase"/>: se o
/// provedor recusa ou não confirma, a tentativa fica registrada e a ocorrência continua aberta; reembolso manual (pago fora do
/// gateway) resolve e guarda o valor para conferência. Null quando a ocorrência não é da empresa.
/// </summary>
public sealed class ResolverOcorrenciaUseCase(
    IOcorrenciaRepository repo,
    ReembolsarPedidoUseCase reembolsar,
    IUnitOfWork unitOfWork,
    TimeProvider relogio)
{
    public async Task<ResolverOcorrenciaResult?> ExecuteAsync(ResolverOcorrenciaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.OcorrenciaId, "OcorrenciaId");
        UseCaseGuards.EnsureNotEmpty(input.UsuarioId, "UsuarioId");
        if (string.IsNullOrWhiteSpace(input.Resolucao))
            throw new UseCaseValidationException("Resolução é obrigatória.");

        if (input.Reembolsar && input.NivelSolicitante is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
            throw new UnauthorizedAccessException("Só a dona ou um gerente pode solicitar reembolso.");
        var ocorrencia = await repo.ObterAsync(input.EmpresaId, input.OcorrenciaId, ct);
        if (ocorrencia is null || ocorrencia.EmpresaId != input.EmpresaId) return null;
        if (!ocorrencia.EstaAberta) throw new UseCaseValidationException("Ocorrência já resolvida.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        ReembolsoResultado? reembolso = null;
        if (input.Reembolsar)
        {
            var valor = input.Valor
                ?? await reembolsar.ValorPagoOnlineAsync(input.EmpresaId, ocorrencia.PedidoId, ocorrencia.Id, ct)
                ?? throw new UseCaseValidationException("Pedido pago fora do gateway: informe o valor do reembolso.");

            reembolso = await reembolsar.ExecuteAsync(ocorrencia, valor, input.Resolucao, agora, input.UsuarioId, input.UsuarioNome, input.NivelSolicitante, ct);
            if (reembolso.Situacao == SituacaoReembolso.Falhou)
                return new ResolverOcorrenciaResult(OcorrenciaDto.De(ocorrencia), reembolso);
        }

        ocorrencia.Resolver(input.Resolucao, input.UsuarioId, agora);
        await unitOfWork.CommitAsync();
        return new ResolverOcorrenciaResult(OcorrenciaDto.De(ocorrencia), reembolso);
    }
}
