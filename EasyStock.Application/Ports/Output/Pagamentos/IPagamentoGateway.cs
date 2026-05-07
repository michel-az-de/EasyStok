using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Contrato generico para gateways de pagamento. Cada provedor (Efi Pix,
/// Efi Boleto, Stripe, MercadoPago, Manual, ...) implementa este contrato
/// via adapter, permitindo plug-and-play sem mexer em UseCases.
///
/// <para>
/// Implementacoes vivem em <c>EasyStock.Infra.Async/Pagamentos/</c>. O
/// roteamento (qual gateway usar para uma fatura) e responsabilidade do
/// <see cref="IPagamentoGatewayRouter"/>.
/// </para>
/// </summary>
public interface IPagamentoGateway
{
    /// <summary>Identificador estavel do provedor — ex: "EfiPix", "EfiBoleto", "Stripe", "Manual".</summary>
    string Provedor { get; }

    /// <summary>Indica se este gateway suporta o metodo informado (ex: "pix", "boleto", "cartao").</summary>
    bool SuportaMetodo(string metodo);

    /// <summary>
    /// Cria uma cobranca no gateway para a fatura. Retorna instrucoes de pagamento
    /// (Pix CopiaCola, URL boleto, etc.) que o cliente vai usar.
    /// </summary>
    /// <param name="fatura">Fatura ja emitida; deve ter <c>Total &gt; 0</c>.</param>
    /// <param name="metodo">Metodo solicitado (deve passar por <see cref="SuportaMetodo"/>).</param>
    Task<InstrucaoPagamento> CriarAsync(Fatura fatura, string metodo, CancellationToken ct = default);

    /// <summary>
    /// Consulta o estado atual de uma transacao no gateway. Usado pela
    /// reconciliacao (F6) quando webhook se perde.
    /// </summary>
    Task<StatusGateway> ConsultarAsync(string transactionId, CancellationToken ct = default);

    /// <summary>
    /// Solicita estorno (parcial ou total) de uma transacao confirmada.
    /// Pode ser sincrono (gateway retorna confirmacao na hora) ou assincrono
    /// (caller deve aguardar webhook de estorno-confirmado).
    /// </summary>
    Task<EstornoResult> EstornarAsync(string transactionId, decimal valor, CancellationToken ct = default);
}
