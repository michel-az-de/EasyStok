using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

/// <param name="UsuarioId">Quem resolveu a ocorrência (vira o autor do lançamento).</param>
public sealed record LancarReembolsoNoCaixaInput(
    Ocorrencia Ocorrencia, ReembolsoResultado Reembolso, Guid? UsuarioId, DateTime Agora);

/// <summary>
/// F14 (#1244), lacuna 3: o dinheiro devolvido na ocorrência (S27, RN-36) sai do caixa do dia como
/// <c>saida</c> com <see cref="Origem"/> = "ocorrencia" e <c>Referencia</c> = id da ocorrência.
/// Efetuado pelo gateway usa o método da cobrança paga; manual (pago fora do gateway) cai em "outro",
/// porque a dona devolve por onde quiser. Recusa do gateway não mexe no caixa.
/// Idempotente por ocorrência. Não faz commit: entra na transação de <see cref="ResolverOcorrenciaUseCase"/>.
/// Não passa pelas guardas de <c>RegistrarMovimentoCaixaUseCase</c> (anti-caixa-negativo): a devolução
/// já aconteceu, o caixa só registra.
/// </summary>
public sealed class LancarReembolsoNoCaixaUseCase(ICaixaRepository caixa, ICobrancaPedidoRepository cobrancas)
{
    public const string Origem = "ocorrencia";
    public const string Categoria = "Reembolso";
    private const string MetodoManual = "outro";

    public async Task<MovimentoCaixa?> ExecuteAsync(LancarReembolsoNoCaixaInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (ocorrencia, reembolso) = (input.Ocorrencia, input.Reembolso);
        if (reembolso.Situacao == SituacaoReembolso.Falhou || reembolso.Valor <= 0m) return null;

        var referencia = ocorrencia.Id.ToString();
        if (await caixa.ExisteMovimentoAsync(ocorrencia.EmpresaId, Origem, referencia, ct)) return null;

        var metodo = reembolso.Situacao == SituacaoReembolso.Efetuado
            ? await MetodoDaCobrancaPagaAsync(ocorrencia, ct) ?? MetodoManual
            : MetodoManual;

        var movimento = MovimentoCaixa.Criar(ocorrencia.EmpresaId, "saida", reembolso.Valor, input.Agora);
        movimento.Metodo = metodo;
        movimento.Categoria = Categoria;
        movimento.Referencia = referencia;
        movimento.Origem = Origem;
        movimento.Descricao = $"Reembolso da ocorrência do pedido {ocorrencia.PedidoId.ToString("N")[..8].ToUpperInvariant()}";
        movimento.RegistradoPorUserId = input.UsuarioId;
        await caixa.AddMovimentoAsync(movimento);
        return movimento;
    }

    private async Task<string?> MetodoDaCobrancaPagaAsync(Ocorrencia ocorrencia, CancellationToken ct) =>
        (await cobrancas.ListarDoPedidoAsync(ocorrencia.EmpresaId, ocorrencia.PedidoId, ct))
            .LastOrDefault(c => c.EhOnline && c.Status is StatusCobrancaPedido.Paga or StatusCobrancaPedido.Estornada)
            ?.MetodoPagamento;
}
