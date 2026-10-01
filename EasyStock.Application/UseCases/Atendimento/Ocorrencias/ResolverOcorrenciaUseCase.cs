using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

/// <param name="Valor">Valor do reembolso; sem valor, devolve o total pago online.</param>
public sealed record ResolverOcorrenciaInput(
    Guid EmpresaId, Guid OcorrenciaId, Guid UsuarioId, string Resolucao, bool Reembolsar, decimal? Valor);

/// <param name="Reembolso">Null quando a resolução não pediu reembolso.</param>
public sealed record ResolverOcorrenciaResult(OcorrenciaDto Ocorrencia, ReembolsoResultado? Reembolso);

/// <summary>
/// Resolução humana da ocorrência (S27, RN-35), só pela dona no console. Sem reembolso grava a
/// resolução e não toca o gateway. Com reembolso chama <see cref="ReembolsarPedidoUseCase"/>: se o
/// gateway recusa, nada é gravado e a ocorrência continua aberta; reembolso manual (pago fora do
/// gateway) resolve e guarda o valor para conferência. Reembolso efetuado ou manual vira saída do
/// caixa do dia (<see cref="LancarReembolsoNoCaixaUseCase"/>, F14). Null quando a ocorrência não é da empresa.
/// </summary>
public sealed class ResolverOcorrenciaUseCase(
    IOcorrenciaRepository repo,
    ReembolsarPedidoUseCase reembolsar,
    LancarReembolsoNoCaixaUseCase lancarNoCaixa,
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

        var ocorrencia = await repo.ObterAsync(input.EmpresaId, input.OcorrenciaId, ct);
        if (ocorrencia is null) return null;
        if (!ocorrencia.EstaAberta) throw new UseCaseValidationException("Ocorrência já resolvida.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        ReembolsoResultado? reembolso = null;
        if (input.Reembolsar)
        {
            var valor = input.Valor
                ?? await reembolsar.ValorPagoOnlineAsync(input.EmpresaId, ocorrencia.PedidoId, ct)
                ?? throw new UseCaseValidationException("Pedido pago fora do gateway: informe o valor do reembolso.");

            reembolso = await reembolsar.ExecuteAsync(ocorrencia, valor, input.Resolucao, agora, ct);
            if (reembolso.Situacao == SituacaoReembolso.Falhou)
                return new ResolverOcorrenciaResult(OcorrenciaDto.De(ocorrencia), reembolso);

            // F14 (#1244): o dinheiro devolvido sai do caixa do dia, no mesmo commit da resolução.
            await lancarNoCaixa.ExecuteAsync(new LancarReembolsoNoCaixaInput(ocorrencia, reembolso, input.UsuarioId, agora), ct);
        }

        ocorrencia.Resolver(input.Resolucao, input.UsuarioId, agora);
        await unitOfWork.CommitAsync();
        return new ResolverOcorrenciaResult(OcorrenciaDto.De(ocorrencia), reembolso);
    }
}
