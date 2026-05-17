using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Adapter stub para Stripe (F12). Demonstra como um gateway internacional
/// se encaixa no contrato <see cref="IPagamentoGateway"/>, mas <b>nao chama
/// a API Stripe</b> — todas as operacoes retornam mensagem "nao configurado"
/// ate que o pacote oficial <c>Stripe.net</c> seja adicionado e as credenciais
/// configuradas em <c>Stripe:SecretKey</c>.
///
/// <para>
/// Quando integrar de verdade:
/// </para>
/// <list type="number">
///   <item>Adicionar <c>&lt;PackageReference Include="Stripe.net" Version="..." /&gt;</c>
///   ao <c>EasyStock.Infra.Async.csproj</c>.</item>
///   <item>No DI, criar <c>StripeConfiguration.ApiKey = config["Stripe:SecretKey"]</c>.</item>
///   <item><c>CriarAsync</c> deve usar <c>PaymentIntentService</c> com modo
///   <c>automatic_payment_methods</c> e retornar <c>client_secret</c> +
///   <c>checkout_url</c> em <c>InstrucaoPagamento.UrlCheckout</c>.</item>
///   <item><c>ConsultarAsync</c>: <c>PaymentIntentService.GetAsync(id)</c>;
///   mapear <c>status</c> (succeeded → Confirmado, etc.).</item>
///   <item><c>EstornarAsync</c>: <c>RefundService.CreateAsync(...)</c>.</item>
///   <item>Adicionar <c>StripeWebhookProcessor</c> + <c>StripeSignatureValidator</c>
///   (este ja existe).</item>
/// </list>
/// </summary>
public sealed class StripeGatewayAdapter(
    IConfiguration configuration,
    ILogger<StripeGatewayAdapter> logger) : IPagamentoGateway
{
    public string Provedor => "Stripe";

    public bool SuportaMetodo(string metodo) =>
        string.Equals(metodo, "cartao", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metodo, "stripe", StringComparison.OrdinalIgnoreCase);

    public Task<InstrucaoPagamento> CriarAsync(Fatura fatura, string metodo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fatura);
        var secretKey = configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            logger.LogWarning(
                "StripeGatewayAdapter.CriarAsync invocado mas Stripe:SecretKey nao configurado. Retornando stub.");
            // Retorna stub que admin precisa completar manualmente — caller
            // deve detectar TransactionId='unconfigured' e nao seguir adiante.
            return Task.FromResult(new InstrucaoPagamento(
                Provedor: Provedor,
                TransactionId: "unconfigured",
                UrlCheckout: null,
                DadosGatewayJson: JsonSerializer.Serialize(new { erro = "Stripe nao configurado." })
            ));
        }

        // TODO(F12-real): substituir por chamada PaymentIntentService.
        throw new NotImplementedException(
            "StripeGatewayAdapter.CriarAsync: pacote Stripe.net nao adicionado. Veja XML doc do adapter.");
    }

    public Task<StatusGateway> ConsultarAsync(string transactionId, CancellationToken ct = default) =>
        Task.FromResult(StatusGateway.Desconhecido);

    public Task<EstornoResult> EstornarAsync(string transactionId, decimal valor, CancellationToken ct = default) =>
        Task.FromResult(new EstornoResult(false, Mensagem: "Stripe estorno nao implementado (stub)."));
}
