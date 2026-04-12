using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace EasyStock.Infra.Postgre.Services;

/// <summary>
/// Implementação streaming de geração de anúncios via OpenAI Chat Completions (SSE).
/// </summary>
internal sealed class GeradorDescricaoAnuncioOpenAIStreaming(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GeradorDescricaoAnuncioOpenAIStreaming> logger) : IGeradorDescricaoAnuncioStreaming
{
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    public async IAsyncEnumerable<string> GerarStreamAsync(
        Produto produto,
        ProdutoVariacao? variacao,
        ItemEstoque? itemEstoque,
        string? instrucoesComplementares = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("OpenAI:ApiKey não configurado. Retornando fallback para produto {ProdutoId}.", produto.Id);
            yield return itemEstoque?.DescricaoAnuncio ?? produto.DescricaoBase ?? produto.Nome;
            yield break;
        }

        var model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var prompt = ConstruirPrompt(produto, variacao, itemEstoque, instrucoesComplementares);

        HttpResponseMessage? response = null;
        string? errorFallback = null;

        try
        {
            var client = httpClientFactory.CreateClient("OpenAI");
            var body = new
            {
                model,
                max_tokens = 512,
                stream = true,
                messages = new[] { new { role = "user", content = prompt } }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = JsonContent.Create(body);

            response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao iniciar stream OpenAI para produto {ProdutoId}.", produto.Id);
            errorFallback = produto.DescricaoBase ?? produto.Nome;
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

    private static string ConstruirPrompt(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Você é um especialista em copywriting para e-commerce. Gere uma descrição de anúncio profissional em português brasileiro para o seguinte produto:");
        sb.AppendLine($"Nome: {produto.Nome}");

        if (!string.IsNullOrWhiteSpace(produto.Marca))
            sb.AppendLine($"Marca: {produto.Marca}");

        if (!string.IsNullOrWhiteSpace(produto.DescricaoBase))
            sb.AppendLine($"Descrição base: {produto.DescricaoBase}");

        if (variacao != null)
        {
            sb.AppendLine($"Variação: {variacao.Nome}");
            if (!string.IsNullOrWhiteSpace(variacao.Cor)) sb.AppendLine($"Cor: {variacao.Cor}");
            if (!string.IsNullOrWhiteSpace(variacao.Tamanho)) sb.AppendLine($"Tamanho: {variacao.Tamanho}");
        }

        if (!string.IsNullOrWhiteSpace(instrucoes))
            sb.AppendLine($"Instruções: {instrucoes}");

        sb.AppendLine();
        sb.AppendLine("Escreva 2-3 parágrafos destacando os principais benefícios. Adequado para marketplace. Responda APENAS com a descrição, sem comentários adicionais.");
        return sb.ToString();
    }
}
