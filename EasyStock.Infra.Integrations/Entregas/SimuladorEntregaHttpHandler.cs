using System.Net;
using System.Net.Http.Json;

namespace EasyStock.Infra.Integrations.Entregas;

/// <summary>API externa fictícia, somente .invalid. Não acessa rede, cobra ou envia entregadores.</summary>
public sealed class SimuladorEntregaHttpHandler(TimeProvider relogio) : HttpMessageHandler
{
    private readonly Dictionary<string, UberCotacaoResponse> _uberCotacoes = [];
    private readonly Dictionary<string, UberEntregaResponse> _uberEntregas = [];
    private readonly Dictionary<string, UberEntregaResponse> _uberIdempotencia = [];
    private readonly Dictionary<string, LalamoveCotacaoResponse> _lalaCotacoes = [];
    private readonly Dictionary<string, LalamoveEntregaResponse> _lalaEntregas = [];
    private readonly SemaphoreSlim _trava = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host != "entregas-simuladas.invalid")
            throw new InvalidOperationException("O simulador só aceita o host entregas-simuladas.invalid.");
        await _trava.WaitAsync(ct);
        try
        {
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/delivery_quotes", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadFromJsonAsync<UberCotacaoRequest>(ct);
                if (body is null) return Erro(HttpStatusCode.BadRequest, "invalid_params");
                var cotacao = new UberCotacaoResponse("dqt_sim_" + Guid.NewGuid().ToString("N"), 1690, "brl",
                    relogio.GetUtcNow().AddMinutes(15), body.PickupReadyDt.AddMinutes(30));
                _uberCotacoes[path[..^"delivery_quotes".Length] + cotacao.Id] = cotacao;
                return Json(HttpStatusCode.OK, cotacao);
            }
            if (path.Contains("/customers/", StringComparison.Ordinal) && path.EndsWith("/deliveries", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadFromJsonAsync<UberEntregaRequest>(ct);
                if (body is null) return Erro(HttpStatusCode.BadRequest, "invalid_params");
                var prefix = path[..^"deliveries".Length];
                if (_uberIdempotencia.TryGetValue(prefix + body.IdempotencyKey, out var anterior)) return Json(HttpStatusCode.OK, anterior);
                if (!_uberCotacoes.TryGetValue(prefix + body.QuoteId, out var cotacao)) return Erro(HttpStatusCode.NotFound, "quote_not_found");
                if (cotacao.Expires <= relogio.GetUtcNow()) return Erro(HttpStatusCode.BadRequest, "quote_expired");
                var entrega = new UberEntregaResponse("del_sim_" + Guid.NewGuid().ToString("N"), "pending", cotacao.Fee, "brl", null, false);
                _uberEntregas[path + "/" + entrega.Id] = entrega;
                _uberIdempotencia[prefix + body.IdempotencyKey] = entrega;
                return Json(HttpStatusCode.OK, entrega);
            }
            if (path.Contains("/customers/", StringComparison.Ordinal))
            {
                var cancelar = path.EndsWith("/cancel", StringComparison.Ordinal) && request.Method == HttpMethod.Post;
                var chave = cancelar ? path[..^"/cancel".Length] : path;
                if (!_uberEntregas.TryGetValue(chave, out var entrega)) return Erro(HttpStatusCode.NotFound, "delivery_not_found");
                if (cancelar) _uberEntregas[chave] = entrega = entrega with { Status = "canceled" };
                else if (request.Method != HttpMethod.Get) return Erro(HttpStatusCode.MethodNotAllowed, "method_not_allowed");
                return Json(HttpStatusCode.OK, entrega);
            }
            if (path == "/v3/quotations" && request.Method == HttpMethod.Post)
            {
                var body = (await request.Content!.ReadFromJsonAsync<LalamoveEnvelope<LalamoveCotacaoRequest>>(ct))?.Data;
                if (body is null || body.Stops.Count is < 2 or > 16) return Erro(HttpStatusCode.UnprocessableEntity, "ERR_TOO_MANY_STOPS");
                var cotacao = new LalamoveCotacaoResponse("sim_" + Guid.NewGuid().ToString("N"), relogio.GetUtcNow().AddMinutes(5),
                    body.Stops.Select((p, i) => p with { StopId = $"{i + 1}" }).ToArray(),
                    new LalamovePreco((12m + 4m * (body.Stops.Count - 1)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture), "BRL"),
                    body.IsRouteOptimized);
                _lalaCotacoes[cotacao.QuotationId] = cotacao;
                return Json(HttpStatusCode.Created, new LalamoveEnvelope<LalamoveCotacaoResponse>(cotacao));
            }
            if (path == "/v3/orders" && request.Method == HttpMethod.Post)
            {
                var body = (await request.Content!.ReadFromJsonAsync<LalamoveEnvelope<LalamoveEntregaRequest>>(ct))?.Data;
                if (body is null || !_lalaCotacoes.TryGetValue(body.QuotationId, out var cotacao)) return Erro(HttpStatusCode.NotFound, "ERR_QUOTATION_NOT_FOUND");
                if (cotacao.ExpiresAt <= relogio.GetUtcNow()) return Erro(HttpStatusCode.UnprocessableEntity, "ERR_QUOTATION_EXPIRED");
                if (body.Sender.StopId != cotacao.Stops[0].StopId || !body.Recipients.Select(r => r.StopId).SequenceEqual(cotacao.Stops.Skip(1).Select(p => p.StopId)))
                    return Erro(HttpStatusCode.UnprocessableEntity, "ERR_INVALID_STOP_ID");
                var entrega = new LalamoveEntregaResponse("sim_" + Guid.NewGuid().ToString("N"), "ASSIGNING_DRIVER", null, cotacao.PriceBreakdown);
                _lalaEntregas[path + "/" + entrega.OrderId] = entrega;
                return Json(HttpStatusCode.Created, new LalamoveEnvelope<LalamoveEntregaResponse>(entrega));
            }
            if (path.StartsWith("/v3/orders/", StringComparison.Ordinal))
            {
                if (!_lalaEntregas.TryGetValue(path, out var entrega)) return Erro(HttpStatusCode.NotFound, "ERR_ORDER_NOT_FOUND");
                if (request.Method == HttpMethod.Delete)
                {
                    _lalaEntregas[path] = entrega with { Status = "CANCELED" };
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (request.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, new LalamoveEnvelope<LalamoveEntregaResponse>(entrega));
            }
            return Erro(HttpStatusCode.NotFound, "simulacao_rota_nao_prevista");
        }
        finally { _trava.Release(); }
    }

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T corpo) => new(status) { Content = JsonContent.Create(corpo) };
    private static HttpResponseMessage Erro(HttpStatusCode status, string codigo) => Json(status, new { code = codigo });
}
