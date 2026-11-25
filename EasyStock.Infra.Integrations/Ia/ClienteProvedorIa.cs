using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Infra.Integrations.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.Ia;

/// <summary>
/// #3064 (S2b de #2948): o provedor de IA da loja, no formato Anthropic Messages, com as travas da spec §6. Cliente HTTP
/// nomeado próprio (<see cref="NomeHttp"/>), separado do cliente do <c>.env</c>: só https na 443 para um host da lista
/// fechada, sem redirect, sem IP privado (checado no <c>ConnectCallback</c>, o que barra DNS rebinding) e resposta
/// limitada. Um disjuntor por provedor (<c>ia:{id}</c>) que o 429 não abre. O corpo de erro sai do log sem a chave.
/// </summary>
public sealed class ClienteProvedorIa(
    IHttpClientFactory fabrica,
    IOptions<AnthropicAgenteOptions> opcoes,
    ResiliencePipelineRegistry<string> pipelines,
    ILogger<ClienteProvedorIa> logger) : IClienteProvedorIa
{
    public const string NomeHttp = "ia-provedor";

    /// <summary>Resposta acima disso é recusada (o padrão do HttpClient é 2 GB).</summary>
    public const int RespostaMaximaBytes = 4 * 1024 * 1024;

    /// <summary>D8: hosts aceitos. Fora da lista, só com a exceção de SuperAdmin que entra com o cadastro da S4.</summary>
    public static readonly IReadOnlySet<string> HostsPermitidos =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "api.anthropic.com", "api.deepseek.com", "api.fireworks.ai" };

    public async Task<RespostaLlm> EnviarAsync(DestinoIa destino, RequisicaoLlm requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(requisicao);
        var endpoint = Endpoint(destino.BaseUrl)
            ?? throw new ProvedorIaIndisponivelException("A URL do provedor de IA não é permitida.");
        var config = opcoes.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (destino.Autenticacao == AutenticacaoProvedorIa.Bearer)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", destino.Chave);
        else
            request.Headers.Add("x-api-key", destino.Chave);
        request.Headers.Add("anthropic-version", AnthropicMessagesClient.AnthropicVersion);
        // #3304 (R8): o teto e o prazo do modelo e do provedor da loja valem; sem eles, o global do .env é a reserva.
        // O teto do modelo é o MÁXIMO que ele aceita: nem o piso da nova tentativa depois de max_tokens passa dele.
        var teto = AnthropicMessagesClient.TetoDeSaida(requisicao, destino.SaidaMax ?? config.MaxTokensAgente);
        if (destino.SaidaMax is { } maximoDoModelo) teto = Math.Min(teto, maximoDoModelo);
        request.Content = new ByteArrayContent(AnthropicMessagesClient.MontarCorpo(requisicao, destino.Modelo, teto));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var http = fabrica.CreateClient(NomeHttp);
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        prazo.CancelAfter(TimeSpan.FromSeconds(destino.PrazoSegundos ?? config.TimeoutSegundos));

        HttpResponseMessage enviada;
        try
        {
            enviada = await Disjuntor(destino.ProvedorId)
                .ExecuteAsync(async pollyCt => await http.SendAsync(request, pollyCt), prazo.Token);
        }
        catch (BrokenCircuitException ex)
        {
            throw new ProvedorIaIndisponivelException("Provedor de IA da loja temporariamente indisponível; nada foi enviado.", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Prazo do provedor, não de quem chamou: conta como falha (Erro), como o timeout do HttpClient do .env.
            throw new HttpRequestException("O provedor de IA da loja não respondeu no prazo.", ex);
        }

        using var response = enviada;
        var corpo = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // Sanitiza antes de cortar: o corte no meio da chave deixaria o começo dela no log.
            var limpo = Sanitizar(corpo, destino.Chave);
            logger.LogWarning("Provedor de IA {ProvedorId} retornou {StatusCode}: {Erro}",
                destino.ProvedorId, (int)response.StatusCode, limpo.Length > 500 ? limpo[..500] : limpo);
            throw new HttpRequestException($"O provedor de IA respondeu {(int)response.StatusCode}.", null, response.StatusCode);
        }

        // #3313: com saida_max cadastrado toda chamada saiu com ele (o piso da nova tentativa não o passa); a geração não repete.
        var resposta = AnthropicMessagesClient.LerResposta(corpo, destino.Modelo) with { TetoNoMaximo = destino.SaidaMax is not null };
        logger.LogInformation("IA uso: provedor={ProvedorId} modelo={Modelo} entrada={Entrada} saida={Saida} cache_lido={CacheLido}",
            destino.ProvedorId, resposta.Modelo, resposta.TokensEntrada, resposta.TokensSaida, resposta.TokensCacheLido);
        return resposta;
    }

    /// <summary>https na 443, sem userinfo, host da lista fechada. Nulo quando qualquer trava falha.</summary>
    internal static Uri? Endpoint(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var raiz)) return null;
        if (raiz.Scheme != Uri.UriSchemeHttps || raiz.Port != 443 || raiz.UserInfo.Length > 0
            || raiz.Query.Length > 0 || raiz.Fragment.Length > 0 || !HostsPermitidos.Contains(raiz.IdnHost))
            return null;
        return new Uri(raiz, "v1/messages");
    }

    internal static string Sanitizar(string texto, string chave) =>
        string.IsNullOrEmpty(chave) ? texto : texto.Replace(chave, "***", StringComparison.Ordinal);

    private ResiliencePipeline Disjuntor(Guid provedorId) =>
        pipelines.GetOrAddPipeline($"ia:{provedorId}", builder => builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 8,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(60),
            // O 429 é limite de concorrência, não queda: não abre o disjuntor (spec §5).
            ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result is not HttpResponseMessage { StatusCode: HttpStatusCode.TooManyRequests }
                && FalhaHttpTransitoria.ContaNoDisjuntor(args.Outcome)),
        }));

    /// <summary>Handler do cliente nomeado: sem redirect e com a conexão só para IP público.</summary>
    internal static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false, // o ConnectCallback valida o IP do provedor; com proxy validaria o do proxy
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = ConectarAsync,
    };

    private static async ValueTask<Stream> ConectarAsync(SocketsHttpConnectionContext contexto, CancellationToken ct)
    {
        var enderecos = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, ct);
        var publicos = enderecos.Where(EnderecoPublico).ToArray();
        if (publicos.Length == 0 || publicos.Length != enderecos.Length)
            throw new HttpRequestException(HttpRequestError.ConnectionError,
                $"O host {contexto.DnsEndPoint.Host} resolve para endereço não público; conexão recusada.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(publicos, contexto.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Recusa loopback, privado, link-local (inclui metadata 169.254.169.254), CGNAT, multicast e não roteável.</summary>
    internal static bool EnderecoPublico(IPAddress endereco)
    {
        if (endereco.IsIPv4MappedToIPv6) endereco = endereco.MapToIPv4();
        if (IPAddress.IsLoopback(endereco)) return false;
        if (endereco.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = endereco.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0));
        }
        if (endereco.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (endereco.Equals(IPAddress.IPv6None) || endereco.Equals(IPAddress.IPv6Any)) return false;
            if (endereco.IsIPv6LinkLocal || endereco.IsIPv6SiteLocal || endereco.IsIPv6Multicast) return false;
            var b = endereco.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false; // fc00::/7, endereço local único
            // Prefixos que embutem IPv4 (podem esconder um privado): 6to4 2002::/16, Teredo 2001::/32, NAT64 64:ff9b::/96.
            if (b[0] == 0x20 && b[1] == 0x02) return false;
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0) return false;
            if (b[0] == 0 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b) return false;
            return true;
        }
        return false;
    }
}
