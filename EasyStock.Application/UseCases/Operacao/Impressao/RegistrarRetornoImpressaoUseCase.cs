using EasyStock.Application.Ports.Output.Persistence.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record RegistrarRetornoImpressaoInput(Guid EmpresaId, Guid ImpressaoId, bool Impressa, string? Erro = null);

/// <summary>
/// Retorno do consumidor da fila (S20): <c>impressa</c> ou <c>falhou</c>. Idempotente: repetir
/// <c>impressa</c> devolve o mesmo estado sem gravar. Devolve <c>null</c> para impressão inexistente ou de
/// outra empresa.
/// </summary>
public sealed class RegistrarRetornoImpressaoUseCase(
    IImpressaoPendenteRepository repo,
    IUnitOfWork unitOfWork,
    TimeProvider relogio,
    ILogger<RegistrarRetornoImpressaoUseCase> logger)
{
    public async Task<ImpressaoPendenteDto?> ExecuteAsync(RegistrarRetornoImpressaoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.ImpressaoId, "ImpressaoId");

        var impressao = await repo.GetByIdAsync(input.EmpresaId, input.ImpressaoId, ct);
        if (impressao is null || impressao.EmpresaId != input.EmpresaId) return null;

        var mudou = input.Impressa
            ? impressao.MarcarImpressa(relogio.GetUtcNow().UtcDateTime)
            : impressao.RegistrarFalha(input.Erro);

        if (mudou)
        {
            await unitOfWork.CommitAsync();
            if (!input.Impressa)
                logger.LogWarning("Impressao falhou impressaoId={ImpressaoId} pedidoId={PedidoId} tentativas={Tentativas}",
                    impressao.Id, impressao.PedidoId, impressao.Tentativas);
        }
        return ImpressaoPendenteDto.De(impressao);
    }
}
