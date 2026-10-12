using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;

namespace EasyStock.Infra.Integrations.Ia;

// O chamador só fornece termos. Hosts e URLs de consulta são fixos, sem fetch de links arbitrários.
public sealed class PesquisaProducaoWeb(HttpClient http) : IPesquisaProducaoWeb
{
    public Task<IReadOnlyList<FonteProducaoWeb>> BuscarInsumosAsync(string texto, CancellationToken ct) =>
        BuscarAsync("pt.wikipedia.org", texto, false, ct);

    public Task<IReadOnlyList<FonteProducaoWeb>> BuscarReceitasAsync(string texto, CancellationToken ct) =>
        BuscarAsync("pt.wikibooks.org", $"Livro de receitas {texto}", true, ct);

    private async Task<IReadOnlyList<FonteProducaoWeb>> BuscarAsync(string host, string texto, bool receitas, CancellationToken ct)
    {
        using var busca = await LerAsync($"https://{host}/w/api.php?action=opensearch&search={Uri.EscapeDataString(texto)}&limit=3&namespace=0&format=json", ct);
        var resultados = new List<FonteProducaoWeb>();
        if (busca.RootElement.ValueKind != JsonValueKind.Array || busca.RootElement.GetArrayLength() != 4 || busca.RootElement[1].ValueKind != JsonValueKind.Array)
            throw new JsonException("Resposta inválida da pesquisa pública.");
        foreach (var tituloJson in busca.RootElement[1].EnumerateArray().Take(3))
        {
            if (tituloJson.ValueKind != JsonValueKind.String) throw new JsonException("Título inválido na pesquisa pública.");
            var titulo = tituloJson.GetString();
            if (string.IsNullOrWhiteSpace(titulo) || titulo.Length > 240 || (receitas && !titulo.StartsWith("Livro de receitas/", StringComparison.Ordinal))) continue;
            var conteudo = "";
            if (receitas)
            {
                using var pagina = await LerAsync($"https://{host}/w/api.php?action=query&titles={Uri.EscapeDataString(titulo)}&prop=extracts&explaintext=1&formatversion=2&format=json", ct);
                if (pagina.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Consulta inválida na pesquisa pública.");
                if (!pagina.RootElement.TryGetProperty("query", out var consulta)) continue;
                if (consulta.ValueKind != JsonValueKind.Object) throw new JsonException("Consulta inválida na pesquisa pública.");
                if (!consulta.TryGetProperty("pages", out var paginas)) continue;
                if (paginas.ValueKind != JsonValueKind.Array) throw new JsonException("Página inválida na pesquisa pública.");
                var primeira = paginas.EnumerateArray().FirstOrDefault();
                if (primeira.ValueKind != JsonValueKind.Object || !primeira.TryGetProperty("extract", out var extrato)) continue;
                if (extrato.ValueKind != JsonValueKind.String) throw new JsonException("Texto inválido na pesquisa pública.");
                conteudo = extrato.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(conteudo)) continue;
                conteudo = conteudo[..Math.Min(conteudo.Length, 6000)];
            }
            resultados.Add(new(titulo, $"https://{host}/wiki/{Uri.EscapeDataString(titulo.Replace(' ', '_'))}", conteudo, DateTimeOffset.UtcNow));
        }
        return resultados;
    }

    private async Task<JsonDocument> LerAsync(string url, CancellationToken ct)
    {
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        prazo.CancelAfter(TimeSpan.FromSeconds(12));
        ct = prazo.Token;
        using var resposta = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resposta.EnsureSuccessStatusCode();
        await using var stream = await resposta.Content.ReadAsStreamAsync(ct);
        using var memoria = new MemoryStream();
        var buffer = new byte[8192];
        int lidos;
        while ((lidos = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (memoria.Length + lidos > 262144) throw new HttpRequestException("A fonte pública excedeu o limite de consulta.");
            memoria.Write(buffer, 0, lidos);
        }
        return JsonDocument.Parse(memoria.ToArray());
    }
}
