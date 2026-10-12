using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

/// <summary>
/// Adapter HTTP direto para a API do MercadoPago (ADR-0005): Preferences do Checkout Pro
/// (<c>checkout/preferences</c>) e Payments (<c>v1/payments</c>, S32).
/// NÃO usa o SDK estático MercadoPago.NET — usa <see cref="HttpClient"/> tipado
/// com timeout configurado externamente (5 s via caller em <c>IniciarCheckoutUseCase</c>).
///
/// <para>
/// Ids que entram no caminho da URL passam por formato fechado (pagamento: só dígitos; preferência:
/// letras, dígitos e hífen): um id vindo de webhook nunca muda o recurso chamado.
/// Nada de token, corpo ou dado do comprador vai para o log.
/// </para>
/// </summary>
public sealed partial class MercadoPagoClient(
    HttpClient httpClient,
    IOptions<MercadoPagoOptions> options,
    ILogger<MercadoPagoClient> logger) : IMercadoPagoClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Campo opcional ausente não vai no corpo (ex.: expiration_date_to sem expiração).
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // #1400: o encoder padrão escreve o + do fuso como + e o Mercado Pago responde 400
        // error_parsing_date. O relaxado mantém + e acentos literais (o corpo não vai para HTML).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex("^[0-9]{1,30}$")]
    private static partial Regex FormatoPagamentoId();

    [GeneratedRegex("^[A-Za-z0-9-]{1,120}$")]
    private static partial Regex FormatoPreferenceId();

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
            // S11: o link vale 30 min (a expiração vem do use case de cobrança).
            expires = command.ExpiraEm.HasValue,
            expiration_date_to = command.ExpiraEm.HasValue ? FormatarDataMp(command.ExpiraEm.Value) : null,
            // Prazo do Pix/boleto gerado dentro do link: sem ele, o QR continua pagável depois do cancelamento.
            date_of_expiration = command.ExpiraEm.HasValue ? FormatarDataMp(command.ExpiraEm.Value) : null,
        };

        // S32: endpoint documentado do Checkout Pro (o antigo v1/payments/preferences nunca foi validado).
        using var request = Requisicao(HttpMethod.Post, "checkout/preferences");
        request.Content = JsonContent.Create(payload, options: JsonOpts);
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            request.Headers.Add("X-Idempotency-Key", command.IdempotencyKey);

        using var response = await httpClient.SendAsync(request, ct);
        await GarantirSucessoAsync(response, ct);

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

    public async Task<PagamentoMercadoPago?> ConsultarPagamentoAsync(string pagamentoId, CancellationToken ct = default)
    {
        var id = ValidarPagamentoId(pagamentoId);

        using var request = Requisicao(HttpMethod.Get, $"v1/payments/{id}");
        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await GarantirSucessoAsync(response, ct);

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return LerPagamento(doc.RootElement);
    }

    public async Task<IReadOnlyList<PagamentoMercadoPago>> BuscarPagamentosPorReferenciaAsync(
        string referenciaExterna, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(referenciaExterna))
            throw new ArgumentException("Referência externa é obrigatória.", nameof(referenciaExterna));

        var url = $"v1/payments/search?external_reference={Uri.EscapeDataString(referenciaExterna.Trim())}" +
                  "&sort=date_created&criteria=desc";
        using var request = Requisicao(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(request, ct);
        await GarantirSucessoAsync(response, ct);

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        return results.EnumerateArray().Select(LerPagamento).ToList();
    }

    public async Task<EstornoMercadoPagoResult> EstornarAsync(
        string pagamentoId, decimal? valor = null, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var id = ValidarPagamentoId(pagamentoId);
        if (valor is <= 0m)
            throw new ArgumentOutOfRangeException(nameof(valor), "Valor do estorno deve ser maior que zero.");

        var chave = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"estorno-{id}-{(valor.HasValue ? valor.Value.ToString("F2", CultureInfo.InvariantCulture) : "total")}"
            : idempotencyKey.Trim();

        using var request = Requisicao(HttpMethod.Post, $"v1/payments/{id}/refunds");
        request.Headers.Add("X-Idempotency-Key", chave);
        // Estorno total vai sem corpo; parcial leva amount (documentação de Reembolsos).
        if (valor.HasValue)
            request.Content = JsonContent.Create(new { amount = valor.Value }, options: JsonOpts);

        using var response = await httpClient.SendAsync(request, ct);
        await GarantirSucessoAsync(response, ct);

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        var estornoId = Texto(root, "id")
            ?? throw new InvalidOperationException("MercadoPago não retornou id do estorno.");

        logger.LogInformation("MP estorno criado paymentId={PaymentId}", id);
        return LerEstorno(root);
    }

    public async Task<EstornoMercadoPagoResult?> ConsultarEstornoAsync(string pagamentoId, string estornoId, CancellationToken ct = default)
    {
        using var request = Requisicao(HttpMethod.Get, $"v1/payments/{ValidarPagamentoId(pagamentoId)}/refunds/{ValidarPagamentoId(estornoId)}");
        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await GarantirSucessoAsync(response, ct);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return LerEstorno(doc.RootElement);
    }

    public async Task<IReadOnlyList<EstornoMercadoPagoResult>> ListarEstornosAsync(string pagamentoId, CancellationToken ct = default)
    {
        using var request = Requisicao(HttpMethod.Get, $"v1/payments/{ValidarPagamentoId(pagamentoId)}/refunds");
        using var response = await httpClient.SendAsync(request, ct);
        await GarantirSucessoAsync(response, ct);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.EnumerateArray().Select(LerEstorno).ToList();
    }

    private static EstornoMercadoPagoResult LerEstorno(JsonElement root) => new(
        Texto(root, "id") ?? throw new InvalidOperationException("Mercado Pago não retornou o identificador do estorno."),
        Numero(root, "amount"), Texto(root, "status"), Texto(root, "payment_id"), Data(root, "date_created"));

    public async Task ExpirarPreferenciaAsync(string preferenceId, DateTime expiraEm, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(preferenceId) || !FormatoPreferenceId().IsMatch(preferenceId))
            throw new ArgumentException("Id de preferência fora do formato.", nameof(preferenceId));

        using var request = Requisicao(HttpMethod.Put, $"checkout/preferences/{preferenceId}");
        request.Content = JsonContent.Create(
            new { expires = true, expiration_date_to = FormatarDataMp(expiraEm) }, options: JsonOpts);

        using var response = await httpClient.SendAsync(request, ct);
        await GarantirSucessoAsync(response, ct);
        logger.LogInformation("MP preference expirada preferenceId={PreferenceId}", preferenceId);
    }

    /// <summary>
    /// Falha HTTP com o motivo do Mercado Pago (#1400): o corpo de erro (sem token nem dado do comprador) vai para a
    /// mensagem da exceção e para o log. Continua <see cref="HttpRequestException"/>, que é o que os chamadores tratam.
    /// </summary>
    private async Task GarantirSucessoAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var corpo = await response.Content.ReadAsStringAsync(ct);
        if (corpo.Length > 500) corpo = corpo[..500];
        var caminho = response.RequestMessage?.RequestUri?.AbsolutePath;
        logger.LogWarning("MP recusou {Metodo} {Caminho}: {Status} {Corpo}",
            response.RequestMessage?.Method, caminho, (int)response.StatusCode, corpo);
        throw new HttpRequestException(
            $"Mercado Pago respondeu {(int)response.StatusCode} ({response.StatusCode}) em {caminho}: {corpo}",
            null, response.StatusCode);
    }

    private HttpRequestMessage Requisicao(HttpMethod metodo, string caminho)
    {
        var request = new HttpRequestMessage(metodo, caminho);
        request.Headers.Add("Authorization", $"Bearer {options.Value.AccessToken}");
        return request;
    }

    private static string ValidarPagamentoId(string pagamentoId)
    {
        if (string.IsNullOrWhiteSpace(pagamentoId) || !FormatoPagamentoId().IsMatch(pagamentoId))
            throw new ArgumentException("Id de pagamento fora do formato (só dígitos).", nameof(pagamentoId));
        return pagamentoId;
    }

    private static PagamentoMercadoPago LerPagamento(JsonElement p) => new(
        Id: Texto(p, "id") ?? throw new InvalidOperationException("MercadoPago não retornou id do pagamento."),
        Status: Texto(p, "status") ?? string.Empty,
        StatusDetail: Texto(p, "status_detail"),
        ExternalReference: Texto(p, "external_reference"),
        TransactionAmount: Numero(p, "transaction_amount") ?? 0m,
        DateApproved: Data(p, "date_approved"),
        PaymentMethodId: Texto(p, "payment_method_id"),
        PaymentTypeId: Texto(p, "payment_type_id"),
        TransactionAmountRefunded: Numero(p, "transaction_amount_refunded") ?? 0);

    /// <summary>String ou número (o Mercado Pago devolve ids numéricos) como texto; null/ausente = null.</summary>
    private static string? Texto(JsonElement e, string campo)
    {
        if (!e.TryGetProperty(campo, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static decimal? Numero(JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private static DateTime? Data(JsonElement e, string campo) =>
        Texto(e, campo) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.UtcDateTime
            : null;

    /// <summary>ISO 8601 com milissegundos e offset, formato que o Mercado Pago aceita (<c>2026-09-29T12:30:00.000+00:00</c>).</summary>
    private static string FormatarDataMp(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
}
