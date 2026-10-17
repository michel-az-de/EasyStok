using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Integrations.Entregas;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Logistica.Lalamove;

/// <summary>
/// Frete pela cotação da Lalamove (#1882): <c>POST /v3/quotations</c> de moto, da cozinha até o cliente,
/// com a chave de PRODUÇÃO da loja (F16). Só cota: não cria pedido nem chama motorista.
/// <list type="bullet">
///   <item>Chave de sandbox nunca cota preço para cliente: devolve <see langword="null"/>.</item>
///   <item>O tipo de moto vem de <c>GET /v3/cities</c> (cache de 12 h), não de enum chutado.</item>
///   <item>A mesma corrida fica 4 min em cache: a Lalamove honra a cotação por 5 e limita 100 por minuto.</item>
///   <item>Qualquer recusa ou falha vira <see langword="null"/> e o frete volta à tabela da loja.</item>
/// </list>
/// </summary>
public sealed class CotadorFreteLalamove(
    HttpClient http,
    IIntegrationCredentialResolver credenciais,
    IMemoryCache cache,
    TimeProvider relogio,
    ILogger<CotadorFreteLalamove> logger) : ICotacaoFreteExterna
{
    public const string HostProducao = Conexao.TestadorLalamove.HostProducao;

    /// <summary>Preferência entre as chaves de moto que a Lalamove usa por mercado; depois, a descrição.</summary>
    private static readonly string[] ChavesMoto = ["MOTORCYCLE", "LALAGO", "MOTO"];

    private static readonly TimeSpan CacheCotacao = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan CacheServico = TimeSpan.FromHours(12);

    public async Task<CotacaoFreteExterna?> CotarAsync(
        Guid empresaId, PontoCotacaoFrete coleta, PontoCotacaoFrete entrega, CancellationToken ct = default)
    {
        var chave = await ChaveDeProducaoAsync(empresaId, ct);
        if (chave is null) return null;

        var chaveCache = $"lalamove:cotacao:{empresaId:N}:{Coordenada(coleta)}:{Coordenada(entrega)}";
        if (cache.TryGetValue(chaveCache, out CotacaoFreteExterna? emCache) && emCache is not null)
            return emCache;

        var cliente = new LalamoveHttpClient(http, chave.Value.ApiKey, chave.Value.Secret, relogio);
        try
        {
            var servico = await ServicoDeMotoAsync(cliente, chave.Value.ApiKey, empresaId, ct);
            if (servico is null) return null;

            var resposta = await cliente.CotarAsync(new LalamoveCotacaoRequest(
                ScheduleAt: null,
                ServiceType: servico,
                Language: "pt_BR",
                Stops: [Parada(coleta), Parada(entrega)],
                IsRouteOptimized: false), ct);

            var cotacao = new CotacaoFreteExterna(CatalogoIntegracoes.Lalamove, Centavos(resposta.PriceBreakdown), Metros(resposta.Distance));
            cache.Set(chaveCache, cotacao, CacheCotacao);
            return cotacao;
        }
        catch (LalamoveErroException ex)
        {
            logger.LogWarning("Lalamove não cotou o frete ({Status} {Erro}); vale a tabela da loja. empresaId={EmpresaId}",
                (int?)ex.StatusCode, ex.ErroId, empresaId);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException or OverflowException or ArgumentException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // Timeout do HttpClient chega como TaskCanceledException sem o ct do caller cancelado.
            logger.LogWarning("Cotação da Lalamove falhou ({Tipo}); vale a tabela da loja. empresaId={EmpresaId}",
                ex.GetType().Name, empresaId);
            return null;
        }
    }

    private async Task<(string ApiKey, string Secret)?> ChaveDeProducaoAsync(Guid empresaId, CancellationToken ct)
    {
        Dictionary<string, string>? campos;
        try
        {
            campos = await credenciais.ObterAsync<Dictionary<string, string>>(
                empresaId, CatalogoIntegracoes.Lalamove, AmbienteIntegracao.Production, ct);
        }
        catch (Exception ex) when (ex is ChaveMestraAusenteException or CryptographicException or JsonException)
        {
            logger.LogWarning("Chave Lalamove ilegível ({Tipo}); frete fica na tabela. empresaId={EmpresaId}", ex.GetType().Name, empresaId);
            return null;
        }
        if (campos is null
            || !campos.TryGetValue(CatalogoIntegracoes.CampoApiKey, out var apiKey) || string.IsNullOrWhiteSpace(apiKey)
            || !campos.TryGetValue(CatalogoIntegracoes.CampoApiSecret, out var secret) || string.IsNullOrWhiteSpace(secret))
            return null;

        // Chave de teste gravada como produção cotaria preço de mentira para o cliente.
        if (apiKey.StartsWith("pk_test", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Chave Lalamove de produção é de teste (pk_test); frete fica na tabela. empresaId={EmpresaId}", empresaId);
            return null;
        }
        return (apiKey.Trim(), secret.Trim());
    }

    private async Task<string?> ServicoDeMotoAsync(LalamoveHttpClient cliente, string apiKey, Guid empresaId, CancellationToken ct)
    {
        var chaveCache = "lalamove:servico-moto:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)))[..16];
        if (cache.TryGetValue(chaveCache, out string? emCache) && emCache is not null)
            return emCache;

        var servico = EscolherMoto(await cliente.ListarCidadesAsync(ct));
        if (servico is null)
        {
            logger.LogWarning("Lalamove não oferece moto para esta conta; frete fica na tabela. empresaId={EmpresaId}", empresaId);
            return null;
        }
        cache.Set(chaveCache, servico, CacheServico);
        return servico;
    }

    public static string? EscolherMoto(IReadOnlyList<LalamoveCidade> cidades)
    {
        var servicos = cidades.SelectMany(c => c.Services ?? []).ToList();
        foreach (var preferida in ChavesMoto)
            if (servicos.FirstOrDefault(s => string.Equals(s.Key, preferida, StringComparison.OrdinalIgnoreCase)) is { } exata)
                return exata.Key;
        return servicos.FirstOrDefault(s =>
            s.Key.Contains("MOTO", StringComparison.OrdinalIgnoreCase)
            || (s.Description?.Contains("moto", StringComparison.OrdinalIgnoreCase) ?? false))?.Key;
    }

    private static LalamoveParada Parada(PontoCotacaoFrete p) => new(
        new LalamoveCoordenadas(p.Lat.ToString("R", CultureInfo.InvariantCulture), p.Lng.ToString("R", CultureInfo.InvariantCulture)),
        p.Endereco);

    private static string Coordenada(PontoCotacaoFrete p) => string.Create(CultureInfo.InvariantCulture, $"{p.Lat:F5},{p.Lng:F5}");

    private static int Centavos(LalamovePreco preco)
    {
        if (!string.Equals(preco.Currency, "BRL", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Cotação fora de reais.");
        return checked((int)Math.Round(decimal.Parse(preco.Total, NumberStyles.Number, CultureInfo.InvariantCulture) * 100m, MidpointRounding.AwayFromZero));
    }

    private static int? Metros(LalamoveDistancia? d) =>
        d is not null && string.Equals(d.Unit, "m", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(d.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : null;
}
