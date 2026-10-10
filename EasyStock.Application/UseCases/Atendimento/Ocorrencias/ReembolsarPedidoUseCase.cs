using EasyStock.Application.UseCases.Pedidos.Cobranca;
using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions.Storefront;

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
/// <see cref="TipoEventoNotificacao.ReembolsoEfetuado"/> para avisar o cliente (outbox, ADR-0030) com
/// <c>telefone</c> (E.164), <c>nome</c> e <c>numero</c> no payload, como o aviso de status do pedido (#1292).
/// Sem telefone válido o aviso não é enfileirado: o reembolso vale do mesmo jeito.
/// Pedido pago fora do gateway responde <see cref="CodigoReembolsoManual"/> e guarda o valor para
/// conferência. Valor maior que o pago é <see cref="UseCaseValidationException"/> (400).
/// A intenção e a confirmação financeira têm commits próprios no serviço de estornos.
/// A resolução e a nota são gravadas por <see cref="ResolverOcorrenciaUseCase"/> após a confirmação.
/// </summary>
public sealed class ReembolsarPedidoUseCase(
    ICobrancaPedidoRepository cobrancas,
    IEstornosOnlineService estornos,
    IClienteCrmRepository crm,
    INotificadorService notificador)
{
    public const string CodigoReembolsoManual = "reembolso_manual_necessario";
    public const string CodigoReembolsoEfetuado = "reembolso_efetuado";
    public const string AutorNota = "sistema";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Valor da operação em retomada ou saldo disponível do recebimento online; null se pago fora do gateway.</summary>
    public async Task<decimal?> ValorPagoOnlineAsync(Guid empresaId, Guid pedidoId, Guid operacaoId, CancellationToken ct = default)
    {
        var dados = await estornos.ConsultarAsync(empresaId, pedidoId, ct);
        return dados.Estornos.FirstOrDefault(e => e.Id == operacaoId)?.Valor
            ?? dados.Pagamentos.LastOrDefault()?.Disponivel;
    }

    public async Task<ReembolsoResultado> ExecuteAsync(
        Ocorrencia ocorrencia, decimal valor, string motivo, DateTime agora, Guid usuarioId, string? usuarioNome, NivelAcesso nivel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ocorrencia);
        if (nivel is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
            throw new UnauthorizedAccessException("Só a dona ou um gerente pode solicitar reembolso.");
        if (valor <= 0m) throw new UseCaseValidationException("Valor do reembolso deve ser maior que zero.");
        if (ocorrencia.ReembolsoEm is not null) throw new UseCaseValidationException("Ocorrência já reembolsada.");

        var dados = await estornos.ConsultarAsync(ocorrencia.EmpresaId, ocorrencia.PedidoId, ct);
        var anterior = dados.Estornos.FirstOrDefault(e => e.Id == ocorrencia.Id);
        var pagamento = anterior is not null ? dados.Pagamentos.Single(p => p.Id == anterior.PagamentoId) : dados.Pagamentos.LastOrDefault();
        if (pagamento is null)
        {
            // Uma cobrança online sem recebimento conciliado não pode virar devolução manual.
            if ((await cobrancas.ListarDoPedidoAsync(ocorrencia.EmpresaId, ocorrencia.PedidoId, ct)).Any(c => c.EhOnline && c.PagamentoExternoId != null))
                throw new UseCaseValidationException("Concilie o recebimento do Mercado Pago antes do reembolso.");
            ocorrencia.RegistrarReembolsoManual(valor);
            return new ReembolsoResultado(SituacaoReembolso.ManualNecessario, CodigoReembolsoManual, valor, null);
        }
        if (anterior is null && valor > pagamento.Disponivel)
            throw new UseCaseValidationException("O valor supera o saldo ainda disponível para devolução.");
        var estorno = await estornos.SolicitarAsync(new(ocorrencia.EmpresaId, ocorrencia.PedidoId, ocorrencia.Id,
            pagamento.Id, valor, motivo, usuarioId, usuarioNome, nivel), ct);
        if (estorno.Situacao != PedidoEstornoOnline.Confirmado)
            return new ReembolsoResultado(SituacaoReembolso.Falhou,
                estorno.Situacao == PedidoEstornoOnline.Pendente ? "estorno_pendente" : "estorno_recusado", valor, estorno.EstornoExternoId);

        ocorrencia.RegistrarReembolso(valor, estorno.EstornoExternoId!, agora);

        var motivoLimpo = string.IsNullOrWhiteSpace(motivo) ? ocorrencia.Relato : motivo.Trim();
        var texto = $"reembolso de {Reais(valor)}: {motivoLimpo}";
        if (texto.Length > ClienteNota.TextoTamanhoMaximo) texto = texto[..ClienteNota.TextoTamanhoMaximo];
        await crm.AdicionarNotaAsync(
            ClienteNota.Criar(ocorrencia.EmpresaId, ocorrencia.ClienteId, texto, AutorNota, agora, ocorrencia.PedidoId), ct);

        var cliente = await crm.ObterComTagsAsync(ocorrencia.EmpresaId, ocorrencia.ClienteId, ct);
        if (TelefoneE164(cliente?.Telefone) is { } telefone)
        {
            var payload = JsonSerializer.Serialize(new
            {
                ocorrenciaId = ocorrencia.Id.ToString(),
                pedidoId = ocorrencia.PedidoId.ToString(),
                clienteId = ocorrencia.ClienteId.ToString(),
                conversaId = ocorrencia.ConversaId?.ToString(),
                telefone,
                nome = PrimeiroNome(cliente!.Nome),
                numero = ocorrencia.PedidoId.ToString("N")[..8].ToUpperInvariant(),
                valor = Reais(valor),
            });
            await notificador.EnfileirarEventoAsync(TipoEventoNotificacao.ReembolsoEfetuado, ocorrencia.EmpresaId, payload, ocorrencia.Id, ct);
        }

        return new ReembolsoResultado(SituacaoReembolso.Efetuado, CodigoReembolsoEfetuado, valor, estorno.EstornoExternoId!);
    }

    private static string? TelefoneE164(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;
        try
        {
            return NormalizadorTelefone.NormalizarE164Br(telefone);
        }
        catch (TelefoneInvalidoException)
        {
            return null;
        }
    }

    private static string PrimeiroNome(string? nome) =>
        string.IsNullOrWhiteSpace(nome) ? "cliente" : nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    private static string Reais(decimal valor) => valor.ToString("C2", PtBr).Replace(' ', ' ');
}
