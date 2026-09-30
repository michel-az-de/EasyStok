using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Adapter do Mercado Pago no roteador de gateways (F12). <c>ConsultarAsync</c> e <c>EstornarAsync</c>
/// delegam ao <see cref="IMercadoPagoClient"/> (S32: <c>GET v1/payments/{id}</c> e
/// <c>POST v1/payments/{id}/refunds</c>). <c>CriarAsync</c> continua não implementado: é o caminho das
/// faturas SaaS, que sai em P02; o pedido é cobrado pela preferência da S11.
/// </summary>
public sealed class MercadoPagoGatewayAdapter(
    IConfiguration configuration,
    IMercadoPagoClient mercadoPagoClient,
    ILogger<MercadoPagoGatewayAdapter> logger) : IPagamentoGateway
{
    public string Provedor => "MercadoPago";

    public bool SuportaMetodo(string metodo) =>
        string.Equals(metodo, "cartao", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metodo, "mercadopago", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metodo, "mp", StringComparison.OrdinalIgnoreCase);

    public Task<InstrucaoPagamento> CriarAsync(
        Fatura fatura,
        string metodo,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fatura);
        // P2: ao implementar, enviar header "X-Idempotency-Key: {idempotencyKey}".
        var token = configuration["MercadoPago:AccessToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning(
                "MercadoPagoGatewayAdapter.CriarAsync invocado mas MercadoPago:AccessToken nao configurado.");
            return Task.FromResult(new InstrucaoPagamento(
                Provedor: Provedor,
                TransactionId: "unconfigured",
                DadosGatewayJson: JsonSerializer.Serialize(new { erro = "MP nao configurado." })
            ));
        }

        throw new NotImplementedException(
            "MercadoPagoGatewayAdapter.CriarAsync: SDK MP nao adicionado. Veja XML doc do adapter.");
    }

    public async Task<StatusGateway> ConsultarAsync(string transactionId, CancellationToken ct = default)
    {
        try
        {
            var pagamento = await mercadoPagoClient.ConsultarPagamentoAsync(transactionId, ct);
            return pagamento?.Status.ToLowerInvariant() switch
            {
                PagamentoMercadoPago.Approved => StatusGateway.Confirmado,
                PagamentoMercadoPago.Pending or PagamentoMercadoPago.InProcess or "authorized" => StatusGateway.Pendente,
                PagamentoMercadoPago.Rejected or PagamentoMercadoPago.Cancelled => StatusGateway.Falhou,
                PagamentoMercadoPago.Refunded or PagamentoMercadoPago.ChargedBack => StatusGateway.Estornado,
                _ => StatusGateway.Desconhecido,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "MercadoPagoGatewayAdapter.ConsultarAsync falhou.");
            return StatusGateway.Desconhecido;
        }
    }

    public async Task<EstornoResult> EstornarAsync(string transactionId, decimal valor, CancellationToken ct = default)
    {
        try
        {
            var estorno = await mercadoPagoClient.EstornarAsync(transactionId, valor, ct: ct);
            return new EstornoResult(true, ProtocoloEstorno: estorno.EstornoId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "MercadoPagoGatewayAdapter.EstornarAsync falhou.");
            return new EstornoResult(false, Mensagem: "Falha ao estornar no Mercado Pago.");
        }
    }
}
