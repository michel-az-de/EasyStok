using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

/// <param name="Valor">Valor da devolução; sem valor, retoma a operação existente ou usa o saldo online.</param>
public sealed record ResolverOcorrenciaInput(
    Guid EmpresaId, Guid OcorrenciaId, Guid UsuarioId, string Resolucao, bool Reembolsar, decimal? Valor,
    NivelAcesso NivelSolicitante = NivelAcesso.Operador, string? UsuarioNome = null);

public sealed record ResolverOcorrenciaResult(OcorrenciaDto Ocorrencia, ReembolsoResultado? Reembolso);

/// <summary>
/// Resolução humana, idempotente por ocorrência e conteúdo. Intenção financeira é persistida antes
/// do HTTP; conclusão, nota e outbox são gravados juntos sob lock, após a confirmação do provedor.
/// O caminho manual legado guarda um valor a conferir e não confirma a devolução do dinheiro.
/// </summary>
public sealed class ResolverOcorrenciaUseCase(
    IOcorrenciaRepository repo, ReembolsarPedidoUseCase reembolsar, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public async Task<ResolverOcorrenciaResult?> ExecuteAsync(ResolverOcorrenciaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ApurarOcorrenciaUseCase.ExigirGerente(input.NivelSolicitante);
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(input.OcorrenciaId, "OcorrenciaId");
        UseCaseGuards.EnsureNotEmpty(input.UsuarioId, "UsuarioId");
        var resolucao = input.Resolucao?.Trim() ?? string.Empty;
        if (resolucao.Length == 0 || resolucao.Length > (input.Reembolsar ? 500 : Ocorrencia.ResolucaoTamanhoMaximo))
            throw new UseCaseValidationException("Informe a resolução dentro do limite de caracteres.");
        if (input.UsuarioNome?.Length > 120) throw new UseCaseValidationException("Nome do responsável inválido.");
        if (!input.Reembolsar && input.Valor is not null)
            throw new UseCaseValidationException("Não informe valor ao encerrar sem solicitar reembolso.");

        var ocorrencia = await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            var o = await repo.ObterTravadaAsync(input.EmpresaId, input.OcorrenciaId, token);
            if (o is null || o.EmpresaId != input.EmpresaId) return null;
            if (!o.EstaAberta) { ConferirRepeticao(o, input, resolucao); return o; }
            var agora = relogio.GetUtcNow().UtcDateTime;
            if (input.Reembolsar)
            {
                var valor = input.Valor ?? (o.ReembolsoSolicitadoEm is not null ? o.ReembolsoValor : null)
                    ?? await reembolsar.ValorPagoOnlineAsync(input.EmpresaId, o.PedidoId, o.Id, token)
                    ?? throw new UseCaseValidationException("Pedido pago fora do gateway: informe o valor do reembolso.");
                if (o.ReembolsoSolicitadoEm is not null)
                {
                    if (o.Resolucao != resolucao || o.ReembolsoValor != valor)
                        throw Conflito("Retome o reembolso com a resolução e o valor já registrados.");
                    return o;
                }
                // Intenção financeira e resolução ficam na mesma transação. O HTTP vem depois.
                if (await reembolsar.ReservarAsync(o, valor, resolucao, input.UsuarioId, input.UsuarioNome, input.NivelSolicitante, token))
                    o.IniciarReembolso(resolucao, valor, agora);
                else
                {
                    o.RegistrarReembolsoManual(valor);
                    o.Resolver(resolucao, input.UsuarioId, agora, input.UsuarioNome);
                    await reembolsar.RegistrarNotaResolucaoAsync(o, input.UsuarioNome, agora, token);
                }
            }
            else
            {
                // Inclui solicitações antigas, anteriores ao campo ReembolsoSolicitadoEm.
                var estorno = await reembolsar.ConsultarReembolsoAsync(o, token);
                if (o.ReembolsoSolicitadoEm is not null || estorno is not null)
                {
                    if (estorno?.Situacao != PedidoEstornoOnline.Recusado)
                        throw Conflito("Há um reembolso em confirmação. Confira a mesma solicitação antes de encerrar.");
                    o.LimparReembolsoRecusado();
                }
                o.Resolver(resolucao, input.UsuarioId, agora, input.UsuarioNome);
                await reembolsar.RegistrarNotaResolucaoAsync(o, input.UsuarioNome, agora, token);
            }
            await unitOfWork.CommitAsync();
            return o;
        }, ct);

        if (ocorrencia is null) return null;
        if (!ocorrencia.EstaAberta) return Anterior(ocorrencia);

        var resultado = await reembolsar.ExecuteAsync(ocorrencia, ocorrencia.ReembolsoValor!.Value, resolucao,
            relogio.GetUtcNow().UtcDateTime, input.UsuarioId, input.UsuarioNome, input.NivelSolicitante, ct);
        return await unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            var o = await repo.ObterTravadaAsync(input.EmpresaId, input.OcorrenciaId, token);
            if (o is null || o.EmpresaId != input.EmpresaId) return null;
            if (!o.EstaAberta) { ConferirRepeticao(o, input, resolucao); return Anterior(o); }
            if (resultado.Situacao != SituacaoReembolso.Efetuado)
                return new ResolverOcorrenciaResult(OcorrenciaDto.De(o), resultado);
            var agora = relogio.GetUtcNow().UtcDateTime;
            o.RegistrarReembolso(resultado.Valor, resultado.IdSolicitacao!, agora);
            o.Resolver(resolucao, input.UsuarioId, agora, input.UsuarioNome);
            await reembolsar.RegistrarConclusaoAsync(o, resultado.Valor, resolucao, agora, token);
            await unitOfWork.CommitAsync();
            return Anterior(o);
        }, ct);
    }

    private static void ConferirRepeticao(Ocorrencia o, ResolverOcorrenciaInput input, string resolucao)
    {
        if (o.Resolucao != resolucao || input.Reembolsar != o.ReembolsoValor.HasValue
            || input.Reembolsar && input.Valor is { } valor && o.ReembolsoValor != valor)
            throw Conflito("A ocorrência já foi encerrada com outra resolução. Atualize o histórico.");
    }

    private static ResolverOcorrenciaResult Anterior(Ocorrencia o) => new(OcorrenciaDto.De(o),
        o.ReembolsoValor is { } valor ? new ReembolsoResultado(
            o.ReembolsoEm is not null ? SituacaoReembolso.Efetuado : SituacaoReembolso.ManualNecessario,
            o.ReembolsoEm is not null ? ReembolsarPedidoUseCase.CodigoReembolsoEfetuado : ReembolsarPedidoUseCase.CodigoReembolsoManual,
            valor, o.ReembolsoIdSolicitacao) : null);

    private static CobrancaPedidoConflitoException Conflito(string mensagem) => new("ocorrencia_em_conflito", mensagem);
}
