using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

/// <summary>
/// Adapter HTTP direto para a Preferences API do MercadoPago (ADR-0005).
/// NÃO usa o SDK estático MercadoPago.NET — usa <see cref="HttpClient"/> tipado
/// com timeout configurado externamente (5 s via caller em <c>IniciarCheckoutUseCase</c>).
/// </summary>
public sealed class MercadoPagoClient(
    HttpClient httpClient,
    IOptions<MercadoPagoOptions> options,
    ILogger<MercadoPagoClient> logger) : IMercadoPagoClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<PreferenceCriadaResult> CriarPreferenceAsync(
        CriarPreferenceCommand command,
        CancellationToken ct = default)
    {
        var payload = new
        {
            items = command.Items.Select(i => new
            {
                title = i.Titulo,
                quantity = i.Quantidade,
                unit_price = i.PrecoUnitario,
                currency_id = "BRL",
            }),
            external_reference = command.PedidoId.ToString(),
            notification_url = options.Value.NotificationUrl,
            back_urls = new
            {
                success = options.Value.BackUrlSuccess,
                failure = options.Value.BackUrlFailure,
                pending = options.Value.BackUrlPending,
            },
            auto_return = "approved",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/payments/preferences");
        request.Content = JsonContent.Create(payload, options: JsonOpts);
        request.Headers.Add("Authorization", $"Bearer {options.Value.AccessToken}");

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var id = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("MercadoPago não retornou id da preference.");

        var initPoint = doc.RootElement.GetProperty("init_point").GetString()
            ?? throw new InvalidOperationException("MercadoPago não retornou init_point.");

        logger.LogInformation(
            "MP preference criada pedidoId={PedidoId} preferenceId={PreferenceId}",
            command.PedidoId, id);

        return new PreferenceCriadaResult(id, initPoint);
    }
}
