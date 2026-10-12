using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Enums.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

public sealed record RegistrarRetornoImpressaoInput(Guid EmpresaId, Guid ImpressaoId, bool Impressa, string? Erro = null,
    Guid? UsuarioId = null, Guid? CanhotoId = null);

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
    public Task<ImpressaoPendenteDto?> ExecuteAsync(RegistrarRetornoImpressaoInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.ImpressaoId, "ImpressaoId");

        // Uma falha de COMMIT exige nova requisição/contexto, nunca retry com entidades já marcadas no tracker.
        return unitOfWork.ExecuteInTransactionSemRetryAsync<ImpressaoPendenteDto?>(async token =>
        {
            // Ordem estável evita deadlock entre documento e canhoto. O lock protege sucesso de falha concorrente.
            var ids = new[] { input.ImpressaoId, input.CanhotoId ?? input.ImpressaoId }.Distinct().Order().ToArray();
            var registros = new List<ImpressaoPendente>();
            foreach (var id in ids)
            {
                var registro = await repo.GetByIdComLockAsync(input.EmpresaId, id, token);
                if (registro is null || registro.EmpresaId != input.EmpresaId) return null;
                registros.Add(registro);
            }
            var impressao = registros.Single(i => i.Id == input.ImpressaoId);
            var canhoto = input.CanhotoId is { } fila ? registros.Single(i => i.Id == fila) : null;
            if (canhoto is not null && (!input.Impressa || impressao.Tipo != TipoImpressao.Documento
                || impressao.Documento != "cozinha" || impressao.OriginalId is not null
                || canhoto.Tipo != TipoImpressao.Canhoto || canhoto.PedidoId != impressao.PedidoId))
                throw new UseCaseValidationException("O canhoto da fila não corresponde a este documento de cozinha.");
            var agora = relogio.GetUtcNow().UtcDateTime;
            var mudou = input.Impressa ? impressao.MarcarImpressa(agora, input.UsuarioId) : impressao.RegistrarFalha(input.Erro);
            if (canhoto is not null) mudou |= canhoto.MarcarImpressa(agora, input.UsuarioId);
            if (mudou)
            {
                await unitOfWork.CommitAsync();
                if (!input.Impressa) logger.LogWarning("Impressao falhou impressaoId={ImpressaoId} pedidoId={PedidoId} tentativas={Tentativas}",
                    impressao.Id, impressao.PedidoId, impressao.Tentativas);
            }
            return ImpressaoPendenteDto.De(impressao);
        }, ct);
    }
}
