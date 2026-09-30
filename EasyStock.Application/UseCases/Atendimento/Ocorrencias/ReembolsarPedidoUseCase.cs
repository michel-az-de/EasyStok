using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.UseCases.Atendimento.Ocorrencias;

public enum SituacaoReembolso
{
    Efetuado,
    ManualNecessario,
    Falhou,
}

/// <param name="Codigo"><c>reembolso_efetuado</c>, <c>reembolso_manual_necessario</c> ou o erro do gateway.</param>
public sealed record ReembolsoResultado(SituacaoReembolso Situacao, string Codigo, decimal Valor, string? IdSolicitacao);

/// <summary>
/// Devolve o dinheiro do pedido da ocorrência (S27, RN-36). Busca a <see cref="CobrancaPedido"/> online
/// paga (S11) e estorna pelo gateway com <c>X-Idempotency-Key = ocorrenciaId</c> (S32). Sucesso grava o
/// reembolso na ocorrência, cria a <c>ClienteNota</c> "reembolso de R$ X: motivo" e enfileira
/// <see cref="TipoEventoNotificacao.ReembolsoEfetuado"/> para avisar o cliente (outbox, ADR-0030).
/// Pedido pago fora do gateway responde <see cref="CodigoReembolsoManual"/> e guarda o valor para
/// conferência. Valor maior que o pago é <see cref="UseCaseValidationException"/> (400).
/// Não faz commit: é parte da resolução (<see cref="ResolverOcorrenciaUseCase"/>).
/// </summary>
public sealed class ReembolsarPedidoUseCase(
    ICobrancaPedidoRepository cobrancas,
    IEstornoPedidoGateway gateway,
    IClienteCrmRepository crm,
    INotificadorService notificador)
{
    public const string CodigoReembolsoManual = "reembolso_manual_necessario";
    public const string CodigoReembolsoEfetuado = "reembolso_efetuado";
    public const string AutorNota = "sistema";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Total pago online do pedido (base do reembolso sem valor informado); null se pago fora do gateway.</summary>
    public async Task<decimal?> ValorPagoOnlineAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default) =>
        (await CobrancaPagaAsync(empresaId, pedidoId, ct)) is { } c ? c.ValorPago ?? c.Valor : null;

    public async Task<ReembolsoResultado> ExecuteAsync(
        Ocorrencia ocorrencia, decimal valor, string motivo, DateTime agora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ocorrencia);
        if (valor <= 0m) throw new UseCaseValidationException("Valor do reembolso deve ser maior que zero.");
        if (ocorrencia.ReembolsoEm is not null) throw new UseCaseValidationException("Ocorrência já reembolsada.");

        var cobranca = await CobrancaPagaAsync(ocorrencia.EmpresaId, ocorrencia.PedidoId, ct);
        if (cobranca is null)
        {
            ocorrencia.RegistrarReembolsoManual(valor);
            return new ReembolsoResultado(SituacaoReembolso.ManualNecessario, CodigoReembolsoManual, valor, null);
        }

        var pago = cobranca.ValorPago ?? cobranca.Valor;
        if (valor > pago)
            throw new UseCaseValidationException($"Reembolso de {Reais(valor)} maior que o pago ({Reais(pago)}).");

        var estorno = await gateway.EstornarAsync(cobranca.PagamentoExternoId!, valor, ocorrencia.Id.ToString(), ct);
        if (estorno is null || !estorno.Sucesso || string.IsNullOrWhiteSpace(estorno.IdSolicitacao))
            return new ReembolsoResultado(SituacaoReembolso.Falhou, estorno?.Erro ?? "estorno_recusado", valor, null);

        ocorrencia.RegistrarReembolso(valor, estorno.IdSolicitacao, agora);

        var motivoLimpo = string.IsNullOrWhiteSpace(motivo) ? ocorrencia.Relato : motivo.Trim();
        var texto = $"reembolso de {Reais(valor)}: {motivoLimpo}";
        if (texto.Length > ClienteNota.TextoTamanhoMaximo) texto = texto[..ClienteNota.TextoTamanhoMaximo];
        await crm.AdicionarNotaAsync(
            ClienteNota.Criar(ocorrencia.EmpresaId, ocorrencia.ClienteId, texto, AutorNota, agora, ocorrencia.PedidoId), ct);

        var payload = JsonSerializer.Serialize(new
        {
            ocorrenciaId = ocorrencia.Id.ToString(),
            pedidoId = ocorrencia.PedidoId.ToString(),
            clienteId = ocorrencia.ClienteId.ToString(),
            conversaId = ocorrencia.ConversaId?.ToString(),
            valor = Reais(valor),
        });
        await notificador.EnfileirarEventoAsync(TipoEventoNotificacao.ReembolsoEfetuado, ocorrencia.EmpresaId, payload, ocorrencia.Id, ct);

        return new ReembolsoResultado(SituacaoReembolso.Efetuado, CodigoReembolsoEfetuado, valor, estorno.IdSolicitacao);
    }

    private async Task<CobrancaPedido?> CobrancaPagaAsync(Guid empresaId, Guid pedidoId, CancellationToken ct) =>
        (await cobrancas.ListarDoPedidoAsync(empresaId, pedidoId, ct))
            .LastOrDefault(c => c.EhOnline && c.Status == StatusCobrancaPedido.Paga && !string.IsNullOrWhiteSpace(c.PagamentoExternoId));

    private static string Reais(decimal valor) => valor.ToString("C2", PtBr).Replace(' ', ' ');
}
