using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Adapter stub para Mercado Pago (F12). Mesmo padrao do <see cref="StripeGatewayAdapter"/>:
/// sem chamada a API real, retorna "nao configurado" ate que credenciais sejam
/// adicionadas em <c>MercadoPago:AccessToken</c>.
///
/// <para>
/// Integrar de verdade:
/// </para>
/// <list type="number">
///   <item>Adicionar pacote <c>mercadopago-sdk-dotnet</c>.</item>
///   <item>No DI: <c>MercadoPagoConfig.AccessToken = config["MercadoPago:AccessToken"]</c>.</item>
///   <item><c>CriarAsync</c>: <c>PreferenceClient.CreateAsync</c> ou
///   <c>PaymentClient.CreateAsync</c> (Pix nativo MP). Retornar <c>init_point</c>
///   em <c>UrlCheckout</c> ou QR Code Pix.</item>
///   <item><c>ConsultarAsync</c>: <c>PaymentClient.GetAsync(id)</c>; mapear
///   status approved → Confirmado, pending → Pendente, rejected → Falhou.</item>
///   <item><c>EstornarAsync</c>: <c>PaymentRefundClient.RefundAsync(...)</c>.</item>
/// </list>
/// </summary>
public sealed class MercadoPagoGatewayAdapter(
    IConfiguration configuration,
    ILogger<MercadoPagoGatewayAdapter> logger) : IPagamentoGateway
{
    public string Provedor => "MercadoPago";

    public bool SuportaMetodo(string metodo) =>
        string.Equals(metodo, "cartao", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metodo, "mercadopago", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metodo, "mp", StringComparison.OrdinalIgnoreCase);

    public Task<InstrucaoPagamento> CriarAsync(Fatura fatura, string metodo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fatura);
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

    public Task<StatusGateway> ConsultarAsync(string transactionId, CancellationToken ct = default) =>
        Task.FromResult(StatusGateway.Desconhecido);

    public Task<EstornoResult> EstornarAsync(string transactionId, decimal valor, CancellationToken ct = default) =>
        Task.FromResult(new EstornoResult(false, Mensagem: "MercadoPago estorno nao implementado (stub)."));
}
