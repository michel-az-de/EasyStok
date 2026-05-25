using EasyStock.Application.Ports.Output.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace EasyStock.Infra.Postgre.Services;

/// <summary>
/// Gera sugestão de descrição de produto via Anthropic Claude sem necessitar de ProdutoId.
/// Usado no formulário de cadastro de novos produtos.
/// </summary>
internal sealed class GeradorAutoPreenchimentoClaude(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GeradorAutoPreenchimentoClaude> logger) : IGeradorAutoPreenchimento
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ModelId = "claude-haiku-4-5-20251001";

    public async IAsyncEnumerable<string> GerarDescricaoProdutoStreamAsync(
        string nomeProduto,
        string? categoria,
        string? marca,
        string? instrucoes,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Anthropic:ApiKey não configurado. Retornando stub para auto-preenchimento.");
            yield return $"Produto de qualidade: {nomeProduto}.";
            yield break;
        }

        var prompt = ConstruirPrompt(nomeProduto, categoria, marca, instrucoes);
        var response = await TentarIniciarStreamAsync(apiKey, prompt, nomeProduto, ct);

        if (response is null)
        {
            yield return $"Descrição não disponível para: {nomeProduto}.";
            yield break;
        }

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
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
                    var type = json.RootElement.GetProperty("type").GetString();
                    if (type == "content_block_delta")
                    {
                        text = json.RootElement
                            .GetProperty("delta")
                            .GetProperty("text")
                            .GetString();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Falha ao parsear chunk SSE Anthropic: {Data}", data);
                }

                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
        }
    }

    private async Task<HttpResponseMessage?> TentarIniciarStreamAsync(
        string apiKey, string prompt, string nomeProduto, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("Anthropic");
            var requestBody = new
            {
                model = ModelId,
                max_tokens = 256,
                stream = true,
                system = "Você é um especialista em cadastro de produtos para e-commerce. Responda APENAS com a descrição solicitada, sem prefácios, sem markdown, sem listas. Texto corrido em português brasileiro.",
                messages = new[] { new { role = "user", content = prompt } }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = JsonContent.Create(requestBody);

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                var logLevel = (int)response.StatusCode >= 500 ? Microsoft.Extensions.Logging.LogLevel.Error : Microsoft.Extensions.Logging.LogLevel.Warning;
                logger.Log(logLevel, "Anthropic API retornou {StatusCode} para auto-preenchimento de '{Nome}': {ErrorBody}",
                    (int)response.StatusCode, nomeProduto, errorBody);
                return null;
            }

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao iniciar stream Anthropic para auto-preenchimento: {Nome}", nomeProduto);
            return null;
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
