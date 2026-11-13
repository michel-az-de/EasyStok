using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.WhatsApp.Waha;

/// <summary>
/// Botões na ponte (#2072). Conta comum do WhatsApp não manda botão interativo: as opções saem numeradas no texto e a
/// resposta "2" (ou o título da opção) volta para o atendimento como o clique no segundo botão. O menu fica guardado
/// por contato durante 24 h e vale para uma resposta só. O cache é o da aplicação: com Redis vale entre a API e o
/// Worker; sem Redis, só no processo que mandou o menu, e a resposta de um menu do Worker chega ao agente como texto.
/// </summary>
public sealed class MenuNumeradoWaha(IDistributedCache cache, IOptions<WahaOptions> options, ILogger<MenuNumeradoWaha> logger)
{
    public const string Instrucao = "Responda com o número da opção.";

    private static readonly DistributedCacheEntryOptions Validade = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };

    public sealed record Opcao(string Id, string Titulo);

    public static string Formatar(string corpo, IReadOnlyList<(string Id, string Titulo)> opcoes)
    {
        var texto = new StringBuilder(corpo.TrimEnd());
        texto.Append('\n');
        for (var i = 0; i < opcoes.Count; i++)
            texto.Append('\n').Append(i + 1).Append(" - ").Append(opcoes[i].Titulo);
        texto.Append("\n\n").Append(Instrucao);
        return texto.ToString();
    }

    public async Task GuardarAsync(string contato, IReadOnlyList<(string Id, string Titulo)> opcoes, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(opcoes.Select(o => new Opcao(o.Id, o.Titulo)).ToList());
        try
        {
            await cache.SetStringAsync(Chave(contato), json, Validade, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ponte WAHA: menu numerado não guardado; a resposta chega ao agente como texto.");
        }
    }

    /// <summary>A opção que o texto do cliente escolhe no menu pendente, ou <c>null</c>. Escolha válida consome o menu.</summary>
    public async Task<Opcao?> EscolhaAsync(string contato, string? texto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        List<Opcao> opcoes;
        try
        {
            var json = await cache.GetStringAsync(Chave(contato), ct);
            if (string.IsNullOrEmpty(json)) return null;
            opcoes = JsonSerializer.Deserialize<List<Opcao>>(json) ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ponte WAHA: menu numerado ilegível no cache; a resposta segue como texto.");
            return null;
        }

        var escolha = Interpretar(texto, opcoes);
        if (escolha is null) return null;

        try
        {
            await cache.RemoveAsync(Chave(contato), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ponte WAHA: menu numerado não removido do cache.");
        }

        return escolha;
    }

    /// <summary>"2", "2.", "2)", "opção 2", "2️⃣" ou o título exato (sem diferença de acento e caixa).</summary>
    public static Opcao? Interpretar(string texto, IReadOnlyList<Opcao> opcoes)
    {
        if (opcoes.Count == 0) return null;

        var limpo = texto.Replace("️", string.Empty, StringComparison.Ordinal)
            .Replace("⃣", string.Empty, StringComparison.Ordinal)
            .Trim().TrimEnd('.', ')', '!', ' ').Trim();
        var semAcento = Normalizar(limpo);
        foreach (var prefixo in new[] { "opcao ", "opcao: ", "op " })
        {
            if (semAcento.StartsWith(prefixo, StringComparison.Ordinal))
            {
                semAcento = semAcento[prefixo.Length..].Trim();
                limpo = semAcento;
                break;
            }
        }

        if (int.TryParse(limpo, NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
            && numero >= 1 && numero <= opcoes.Count)
            return opcoes[numero - 1];

        var porTitulo = opcoes.Where(o => Normalizar(o.Titulo) == semAcento).ToList();
        return porTitulo.Count == 1 ? porTitulo[0] : null;
    }

    private static string Normalizar(string texto)
    {
        var decomposto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var semMarcas = decomposto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string(semMarcas.ToArray()).Normalize(NormalizationForm.FormC);
    }

    private string Chave(string contato) => $"waha:{options.Value.Sessao}:menu:{WahaIds.Usuario(contato)}";
}
