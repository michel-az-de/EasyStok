using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.WhatsApp.Waha;

/// <summary>
/// Traduz a conversa do WAHA para o contato do EasyStok (só dígitos, como o <c>wa_id</c> da Meta) e de volta.
/// O WhatsApp entrega parte das conversas pelo id oculto (<c>...@lid</c>), sem o telefone: o WAHA conhece o telefone
/// da maioria (<c>GET /api/{sessao}/lids/{lid}</c>) e o par fica guardado. Id oculto sem telefone vira o próprio
/// contato e fica marcado, para a resposta sair pela mesma conversa.
/// </summary>
public sealed class ResolvedorContatoWaha(
    HttpClient http,
    IOptions<WahaOptions> options,
    IDistributedCache cache,
    ILogger<ResolvedorContatoWaha> logger)
{
    private static readonly DistributedCacheEntryOptions Validade = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) };

    public sealed record ContatoWaha(string Contato, string Conversa);

    /// <summary><c>null</c> para o que não é conversa com uma pessoa (grupo, status, canal).</summary>
    public async Task<ContatoWaha?> ContatoAsync(string? conversa, CancellationToken ct)
    {
        if (WahaIds.EhTelefone(conversa))
        {
            var telefone = WahaIds.Usuario(conversa);
            return telefone.Length == 0 ? null : new ContatoWaha(telefone, WahaIds.ConversaDoContato(telefone));
        }

        if (!WahaIds.EhLid(conversa)) return null;
        var lid = WahaIds.Usuario(conversa);
        if (lid.Length == 0) return null;

        var conhecido = await LerAsync(ChaveTelefone(lid), ct);
        if (!string.IsNullOrEmpty(conhecido)) return new ContatoWaha(conhecido, WahaIds.ConversaDoContato(conhecido));

        var achado = await BuscarTelefoneAsync(lid, ct);
        if (achado is not null)
        {
            await GravarAsync(ChaveTelefone(lid), achado, ct);
            return new ContatoWaha(achado, WahaIds.ConversaDoContato(achado));
        }

        await GravarAsync(ChaveLidSemTelefone(lid), "1", ct);
        logger.LogWarning("Ponte WAHA: conversa por id oculto sem telefone conhecido; o contato fica com o id oculto.");
        return new ContatoWaha(lid, WahaIds.ConversaDoContato(lid, ehLid: true));
    }

    /// <summary>Conversa do WAHA para enviar ao contato do EasyStok.</summary>
    public async Task<string> ConversaAsync(string contato, CancellationToken ct)
    {
        if (contato.Contains('@', StringComparison.Ordinal)) return contato;
        var digitos = WahaIds.Usuario(contato);
        var ehLid = !string.IsNullOrEmpty(await LerAsync(ChaveLidSemTelefone(digitos), ct));
        return WahaIds.ConversaDoContato(digitos, ehLid);
    }

    private async Task<string?> BuscarTelefoneAsync(string lid, CancellationToken ct)
    {
        var sessao = Uri.EscapeDataString(options.Value.Sessao);
        var alvo = Uri.EscapeDataString(lid + WahaIds.SufixoLid);
        try
        {
            using var resposta = await http.GetAsync($"api/{sessao}/lids/{alvo}", ct);
            if (!resposta.IsSuccessStatusCode)
            {
                logger.LogWarning("Ponte WAHA: consulta do telefone do id oculto respondeu HTTP {Status}.", (int)resposta.StatusCode);
                return null;
            }

            var corpo = await resposta.Content.ReadFromJsonAsync<LidTelefone>(ct);
            return WahaIds.EhTelefone(corpo?.Pn) ? WahaIds.Usuario(corpo!.Pn) : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Ponte WAHA: falha ao consultar o telefone do id oculto.");
            return null;
        }
    }

    // Cache fora do ar não derruba o atendimento: sem o par lembrado, a conversa segue pelo que o WAHA mandou.
    private async Task<string?> LerAsync(string chave, CancellationToken ct)
    {
        try
        {
            return await cache.GetStringAsync(chave, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ponte WAHA: cache indisponível na leitura.");
            return null;
        }
    }

    private async Task GravarAsync(string chave, string valor, CancellationToken ct)
    {
        try
        {
            await cache.SetStringAsync(chave, valor, Validade, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ponte WAHA: cache indisponível na gravação.");
        }
    }

    private string ChaveTelefone(string lid) => $"waha:{options.Value.Sessao}:lid:{lid}:telefone";

    private string ChaveLidSemTelefone(string lid) => $"waha:{options.Value.Sessao}:lid:{lid}:sem-telefone";

    private sealed record LidTelefone([property: JsonPropertyName("lid")] string? Lid, [property: JsonPropertyName("pn")] string? Pn);
}
