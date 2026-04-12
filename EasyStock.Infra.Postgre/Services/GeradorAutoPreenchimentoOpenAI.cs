using EasyStock.Application.Ports.Output.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace EasyStock.Infra.Postgre.Services;

/// <summary>
/// Gera sugestão de descrição de produto via OpenAI sem necessitar de ProdutoId.
/// Usado no formulário de cadastro de novos produtos.
/// </summary>
internal sealed class GeradorAutoPreenchimentoOpenAI(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GeradorAutoPreenchimentoOpenAI> logger) : IGeradorAutoPreenchimento
{
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    public async IAsyncEnumerable<string> GerarDescricaoProdutoStreamAsync(
        string nomeProduto,
        string? categoria,
        string? marca,
        string? instrucoes,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("OpenAI:ApiKey não configurado. Retornando stub para auto-preenchimento.");
            yield return $"Produto de qualidade: {nomeProduto}.";
            yield break;
        }

        var model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var prompt = ConstruirPrompt(nomeProduto, categoria, marca, instrucoes);

        HttpResponseMessage? response = null;
        string? errorFallback = null;

        try
        {
            var client = httpClientFactory.CreateClient("OpenAI");
            var body = new
            {
                model,
                max_tokens = 256,
                stream = true,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Você é um especialista em cadastro de produtos para e-commerce. Responda APENAS com a descrição solicitada, sem prefácios, sem markdown, sem listas. Texto corrido em português brasileiro."
                    },
                    new { role = "user", content = prompt }
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = JsonContent.Create(body);

            response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao iniciar stream OpenAI para auto-preenchimento: {Nome}", nomeProduto);
            errorFallback = $"Descrição não disponível para: {nomeProduto}.";
        }

        if (errorFallback is not null)
        {
            yield return errorFallback;
            yield break;
        }

        using (response)
        {
            await using var stream = await response!.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:")) continue;

                var data = line["data:".Length..].Trim();
                if (data == "[DONE]") break;

                string? text = null;
                try
                {
                    var json = JsonDocument.Parse(data);
                    var choices = json.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var content))
                            text = content.GetString();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Falha ao parsear chunk SSE OpenAI: {Data}", data);
                }

                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
        }
    }

    private static string ConstruirPrompt(string nome, string? categoria, string? marca, string? instrucoes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Gere uma descrição comercial curta e objetiva (2 a 3 frases) para o seguinte produto:");
        sb.AppendLine($"Nome: {nome}");
        if (!string.IsNullOrWhiteSpace(categoria)) sb.AppendLine($"Categoria: {categoria}");
        if (!string.IsNullOrWhiteSpace(marca)) sb.AppendLine($"Marca: {marca}");
        if (!string.IsNullOrWhiteSpace(instrucoes)) sb.AppendLine($"Instruções extras: {instrucoes}");
        return sb.ToString();
    }
}
