using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Integration.Logistica;
using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Integrations.Entregas;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Logistica.Lalamove;

/// <summary>
/// Despacho pela API v3 da Lalamove (#2160, ADR-0064) sobre o <see cref="LalamoveHttpClient"/>.
/// <list type="bullet">
///   <item>O ambiente vem da conexão da loja: a de produção, se existir e não for <c>pk_test</c>; senão a de sandbox.</item>
///   <item>Os clientes HTTP não têm retry: o POST de criação nunca é repetido (R3). Timeout vira
///   <see cref="LogisticaIndisponivelException"/> e a reconsulta decide (R4).</item>
///   <item>O tipo de veículo vem de <c>GET /v3/cities</c> (cache de 12 h), como no frete do checkout.</item>
/// </list>
/// </summary>
public sealed class LalamoveLogisticaGateway(
    IHttpClientFactory httpClients,
    IIntegrationCredentialResolver credenciais,
    IMemoryCache cache,
    TimeProvider relogio,
    ILogger<LalamoveLogisticaGateway> logger) : ILogisticaGateway
{
    public const string ClienteProducao = "lalamove-producao";
    public const string ClienteSandbox = "lalamove-sandbox";

    /// <summary>Caminho que a Lalamove assina no webhook: o da URL cadastrada no Partner Portal.</summary>
    public const string CaminhoWebhook = "/api/webhooks/lalamove";

    private static readonly string[] ChavesCarro = ["CAR", "SEDAN", "HATCHBACK", "UV_FIORINO", "VAN"];
    private static readonly TimeSpan CacheServico = TimeSpan.FromHours(12);

    public EmpresaEntregador Provedor => EmpresaEntregador.Lalamove;

    private sealed record Conexao(string ApiKey, string Secret, AmbienteIntegracao Ambiente);

    public async Task<bool> ConectadoAsync(Guid empresaId, CancellationToken ct = default) =>
        await ConexaoAsync(empresaId, ct) is not null;

    public async Task<CotacaoCorrida> CotarAsync(Guid empresaId, PedidoCotacaoCorrida pedido, CancellationToken ct = default)
    {
        var (cliente, conexao) = await ClienteAsync(empresaId, ct);
        return await ChamarAsync(async () =>
        {
            var servico = await ServicoAsync(cliente, conexao, pedido.Veiculo, ct)
                ?? throw new LogisticaRecusadaException(pedido.Veiculo == VeiculoCorrida.Carro
                    ? "A Lalamove não oferece carro para esta conta." : "A Lalamove não oferece moto para esta conta.");
            var resposta = await cliente.CotarAsync(new LalamoveCotacaoRequest(
                pedido.AgendarPara, servico, "pt_BR", [Parada(pedido.Coleta), Parada(pedido.Entrega)], IsRouteOptimized: false), ct);
            return Cotacao(resposta);
        });
    }

    public async Task<CotacaoCorrida> ObterCotacaoAsync(Guid empresaId, string cotacaoId, CancellationToken ct = default)
    {
        var (cliente, _) = await ClienteAsync(empresaId, ct);
        return await ChamarAsync(async () => Cotacao(await cliente.ObterCotacaoAsync(cotacaoId, ct)));
    }

    public async Task<SituacaoCorridaProvedor> ChamarAsync(Guid empresaId, PedidoChamadaCorrida pedido, CancellationToken ct = default)
    {
        var (cliente, _) = await ClienteAsync(empresaId, ct);
        var observacao = pedido.Observacao is { Length: > 0 } o ? " " + o : "";
        return await ChamarAsync(async () => Situacao(await cliente.CriarAsync(new LalamoveEntregaRequest(
            pedido.Cotacao.CotacaoId,
            new LalamoveContato(pedido.Cotacao.ColetaParadaId, pedido.Remetente.Nome, Telefone(pedido.Remetente.Telefone)),
            [new LalamoveContato(pedido.Cotacao.EntregaParadaId, (pedido.Destinatario.Nome + observacao).Trim(), Telefone(pedido.Destinatario.Telefone))],
            Metadata: new Dictionary<string, string> { ["corridaId"] = pedido.CorridaId.ToString() }), ct)));
    }

    public async Task<SituacaoCorridaProvedor?> ConsultarAsync(Guid empresaId, string idExterno, CancellationToken ct = default)
    {
        var (cliente, _) = await ClienteAsync(empresaId, ct);
        try
        {
            return await ChamarAsync(async () => Situacao(await cliente.ConsultarAsync(idExterno, ct)));
        }
        catch (LogisticaRecusadaException ex) when (ex.Codigo is "404" or "ERR_INVALID_ORDER_ID" or "ERR_ORDER_NOT_FOUND")
        {
            return null;
        }
    }

    public async Task<MotoristaCorrida?> ObterMotoristaAsync(Guid empresaId, string idExterno, string motoristaIdExterno, CancellationToken ct = default)
    {
        var (cliente, _) = await ClienteAsync(empresaId, ct);
        try
        {
            var m = await ChamarAsync(() => cliente.ObterMotoristaAsync(idExterno, motoristaIdExterno, ct));
            return new MotoristaCorrida(m.DriverId, m.Name, m.Phone, m.PlateNumber);
        }
        catch (LogisticaRecusadaException)
        {
            // Motorista ainda sem dados: a corrida segue, a próxima reconsulta tenta de novo.
            return null;
        }
    }

    public async Task CancelarAsync(Guid empresaId, string idExterno, CancellationToken ct = default)
    {
        var (cliente, _) = await ClienteAsync(empresaId, ct);
        await ChamarAsync(async () => { await cliente.CancelarAsync(idExterno, ct); return true; });
    }

    public async Task AdicionarGorjetaAsync(Guid empresaId, string idExterno, int centavos, CancellationToken ct = default)
    {
        var (cliente, _) = await ClienteAsync(empresaId, ct);
        var reais = (centavos / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        await ChamarAsync(() => cliente.AdicionarGorjetaAsync(idExterno, reais, ct));
    }

    public async Task<bool> WebhookAutenticoAsync(Guid empresaId, string corpo, CancellationToken ct = default)
    {
        try
        {
            var conexao = await ConexaoAsync(empresaId, ct);
            if (conexao is null) return false;
            using var doc = JsonDocument.Parse(corpo);
            var raiz = doc.RootElement;
            if (!raiz.TryGetProperty("apiKey", out var apiKey) || apiKey.GetString() != conexao.ApiKey) return false;
            if (!raiz.TryGetProperty("timestamp", out var ts) || !raiz.TryGetProperty("signature", out var assinatura)
                || !raiz.TryGetProperty("data", out var data)) return false;
            var timestamp = ts.ValueKind == JsonValueKind.Number ? ts.GetRawText() : ts.GetString() ?? "";
            return AssinadorLalamove.WebhookConfere(conexao.Secret, timestamp, CaminhoWebhook, data.GetRawText(), assinatura.GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<(LalamoveHttpClient Cliente, Conexao Conexao)> ClienteAsync(Guid empresaId, CancellationToken ct)
    {
        var conexao = await ConexaoAsync(empresaId, ct) ?? throw new LogisticaNaoConectadaException("Lalamove");
        var http = httpClients.CreateClient(conexao.Ambiente == AmbienteIntegracao.Production ? ClienteProducao : ClienteSandbox);
        return (new LalamoveHttpClient(http, conexao.ApiKey, conexao.Secret, relogio), conexao);
    }

    private async Task<Conexao?> ConexaoAsync(Guid empresaId, CancellationToken ct)
    {
        foreach (var ambiente in new[] { AmbienteIntegracao.Production, AmbienteIntegracao.Sandbox })
        {
            Dictionary<string, string>? campos;
            try
            {
                campos = await credenciais.ObterAsync<Dictionary<string, string>>(empresaId, CatalogoIntegracoes.Lalamove, ambiente, ct);
            }
            catch (Exception ex) when (ex is ChaveMestraAusenteException or CryptographicException or JsonException)
            {
                logger.LogWarning("Conexão Lalamove ilegível ({Tipo}). empresaId={EmpresaId}", ex.GetType().Name, empresaId);
                continue;
            }
            if (campos is null
                || !campos.TryGetValue(CatalogoIntegracoes.CampoApiKey, out var apiKey) || string.IsNullOrWhiteSpace(apiKey)
                || !campos.TryGetValue(CatalogoIntegracoes.CampoApiSecret, out var secret) || string.IsNullOrWhiteSpace(secret))
                continue;
            // Chave de teste gravada como produção chamaria o host errado.
            if (ambiente == AmbienteIntegracao.Production && apiKey.StartsWith("pk_test", StringComparison.OrdinalIgnoreCase)) continue;
            return new Conexao(apiKey.Trim(), secret.Trim(), ambiente);
        }
        return null;
    }

    private async Task<string?> ServicoAsync(LalamoveHttpClient cliente, Conexao conexao, VeiculoCorrida veiculo, CancellationToken ct)
    {
        var chave = $"lalamove:servico:{veiculo}:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conexao.ApiKey)))[..16];
        if (cache.TryGetValue(chave, out string? emCache) && emCache is not null) return emCache;
        var cidades = await cliente.ListarCidadesAsync(ct);
        var servico = veiculo == VeiculoCorrida.Carro ? EscolherCarro(cidades) : CotadorFreteLalamove.EscolherMoto(cidades);
        if (servico is not null) cache.Set(chave, servico, CacheServico);
        return servico;
    }

    public static string? EscolherCarro(IReadOnlyList<LalamoveCidade> cidades)
    {
        var servicos = cidades.SelectMany(c => c.Services ?? []).ToList();
        foreach (var preferida in ChavesCarro)
            if (servicos.FirstOrDefault(s => string.Equals(s.Key, preferida, StringComparison.OrdinalIgnoreCase)) is { } exata)
                return exata.Key;
        return servicos.FirstOrDefault(s => s.Description?.Contains("carro", StringComparison.OrdinalIgnoreCase) ?? false)?.Key;
    }

    /// <summary>Status da v3 para o domínio. Desconhecido não muda nada: fica como procurando motorista.</summary>
    public static StatusCorrida MapearStatus(string status) => status switch
    {
        "ASSIGNING_DRIVER" => StatusCorrida.ProcurandoMotorista,
        "ON_GOING" => StatusCorrida.MotoristaACaminho,
        "PICKED_UP" => StatusCorrida.Coletada,
        "COMPLETED" => StatusCorrida.Entregue,
        "CANCELED" => StatusCorrida.Cancelada,
        "REJECTED" => StatusCorrida.Rejeitada,
        "EXPIRED" => StatusCorrida.Expirada,
        _ => throw new LogisticaRecusadaException($"Status da Lalamove desconhecido: {status}."),
    };

    private static SituacaoCorridaProvedor Situacao(LalamoveEntregaResponse r) => new(
        r.OrderId, MapearStatus(r.Status), r.ShareLink, Centavos(r.PriceBreakdown), string.IsNullOrWhiteSpace(r.DriverId) ? null : r.DriverId,
        r.Metadata is not null && r.Metadata.TryGetValue("corridaId", out var id) && Guid.TryParse(id, out var guid) ? guid : null);

    private static CotacaoCorrida Cotacao(LalamoveCotacaoResponse r)
    {
        if (r.Stops.Count < 2 || r.Stops[0].StopId is not { } coleta || r.Stops[^1].StopId is not { } entrega)
            throw new LogisticaRecusadaException("A cotação da Lalamove veio sem as paradas.");
        var metros = r.Distance is { Unit: "m" } d && int.TryParse(d.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : (int?)null;
        return new CotacaoCorrida(r.QuotationId, Centavos(r.PriceBreakdown), r.ExpiresAt, metros, coleta, entrega);
    }

    private static int Centavos(LalamovePreco preco)
    {
        if (!string.Equals(preco.Currency, "BRL", StringComparison.OrdinalIgnoreCase))
            throw new LogisticaRecusadaException("A Lalamove cotou fora de reais.");
        return checked((int)Math.Round(decimal.Parse(preco.Total, NumberStyles.Number, CultureInfo.InvariantCulture) * 100m, MidpointRounding.AwayFromZero));
    }

    private static LalamoveParada Parada(PontoCorrida p) => new(
        new LalamoveCoordenadas(p.Lat.ToString("R", CultureInfo.InvariantCulture), p.Lng.ToString("R", CultureInfo.InvariantCulture)), p.Endereco);

    /// <summary>A Lalamove pede o telefone em E.164; o cadastro guarda só dígitos, às vezes sem o 55.</summary>
    internal static string Telefone(string telefone)
    {
        var digitos = new string(telefone.Where(char.IsDigit).ToArray());
        return "+" + (digitos.Length is 10 or 11 ? "55" + digitos : digitos);
    }

    /// <summary>Recusa 4xx vira mensagem para o operador; timeout e 5xx, indisponibilidade. O corpo nunca sai.</summary>
    private async Task<T> ChamarAsync<T>(Func<Task<T>> chamada)
    {
        try
        {
            return await chamada();
        }
        catch (LalamoveErroException ex) when (ex.StatusCode is { } s && (int)s is >= 400 and < 500)
        {
            logger.LogWarning("Lalamove recusou ({Status} {Erro}).", (int)s, ex.ErroId);
            throw new LogisticaRecusadaException(Mensagem(s, ex.ErroId), ex.ErroId ?? ((int)s).ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("Lalamove indisponível ({Tipo}).", ex.GetType().Name);
            throw new LogisticaIndisponivelException("A Lalamove não respondeu agora. Tente de novo em instantes.", ex);
        }
    }

    private static string Mensagem(HttpStatusCode status, string? erro) => erro switch
    {
        "ERR_OUT_OF_SERVICE_AREA" => "Endereço fora da área atendida pela Lalamove.",
        "ERR_INSUFFICIENT_CREDIT" => "A carteira da Lalamove está sem saldo. Recarregue no painel da Lalamove.",
        "ERR_QUOTATION_EXPIRED" => "A cotação venceu. Cote de novo para ver o preço atual.",
        "ERR_CANCELLATION_FORBIDDEN" => "A Lalamove não deixa mais cancelar esta corrida.",
        "ERR_INVALID_PHONE_NUMBER" => "Telefone recusado pela Lalamove. Confira o telefone da loja e o do cliente.",
        _ when status == HttpStatusCode.PaymentRequired => "A carteira da Lalamove está sem saldo. Recarregue no painel da Lalamove.",
        _ when status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "A Lalamove recusou a chave da loja. Confira a conexão em Gestão > Integrações.",
        _ => $"A Lalamove recusou o pedido ({erro ?? ((int)status).ToString(CultureInfo.InvariantCulture)}).",
    };
}
