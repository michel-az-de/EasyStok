using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp.Waha;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// #2515: menu numerado pendente da conexão provisória, por sessão e contato, no cache da aplicação (24 h, uma escolha).
/// A interpretação do "2" é a mesma da ponte WAHA (#2072). O webhook e o turno do agente rodam na API, então o cache em
/// memória também serve.
/// </summary>
public sealed class MenuNumeradoWppConnect(IDistributedCache cache, ILogger<MenuNumeradoWppConnect> logger) : IMenusNumeradosWppConnect
{
    private static readonly DistributedCacheEntryOptions Validade = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };

    public async Task GuardarAsync(string sessao, string contato, IReadOnlyList<(string Id, string Titulo)> opcoes, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(opcoes.Select(o => new MenuNumeradoWaha.Opcao(o.Id, o.Titulo)).ToList());
        try
        {
            await cache.SetStringAsync(Chave(sessao, contato), json, Validade, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "WPPConnect: menu numerado não guardado; a resposta chega ao agente como texto.");
        }
    }

    public async Task<(string Id, string Titulo)?> EscolhaAsync(string sessao, string contato, string? texto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var chave = Chave(sessao, contato);
        List<MenuNumeradoWaha.Opcao> opcoes;
        try
        {
            var json = await cache.GetStringAsync(chave, ct);
            if (string.IsNullOrEmpty(json)) return null;
            opcoes = JsonSerializer.Deserialize<List<MenuNumeradoWaha.Opcao>>(json) ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "WPPConnect: menu numerado ilegível no cache; a resposta segue como texto.");
            return null;
        }

        if (MenuNumeradoWaha.Interpretar(texto, opcoes) is not { } escolha) return null;
        try
        {
            await cache.RemoveAsync(chave, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "WPPConnect: menu numerado não removido do cache.");
        }
        return (escolha.Id, escolha.Titulo);
    }

    private static string Chave(string sessao, string contato) =>
        $"wpp:{sessao}:menu:{new string(contato.Where(char.IsAsciiDigit).ToArray())}";
}
