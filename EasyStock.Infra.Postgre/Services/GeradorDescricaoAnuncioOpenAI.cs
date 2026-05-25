using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyStock.Infra.Postgre.Services;

/// <summary>
/// Implementação não-streaming de geração de descrições via OpenAI.
/// </summary>
internal sealed class GeradorDescricaoAnuncioOpenAI(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GeradorDescricaoAnuncioOpenAI> logger) : IGeradorDescricaoAnuncio
{
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    public async Task<string> GerarAsync(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoesComplementares = null)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("OpenAI:ApiKey não configurado. Usando fallback para produto {ProdutoId}.", produto.Id);
            return ObterDescricaoFallback(produto, itemEstoque);
        }

        var model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";

        try
        {
            var prompt = ConstruirPrompt(produto, variacao, itemEstoque, instrucoesComplementares);
            var client = httpClientFactory.CreateClient("OpenAI");

            var body = new
            {
                model,
                max_tokens = 512,
                messages = new[] { new { role = "user", content = prompt } }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = JsonContent.Create(body);

            var response = await client.SendAsync(req);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var text = json
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            logger.LogInformation("Descrição gerada via OpenAI para produto {ProdutoId}.", produto.Id);
            return text ?? produto.Nome;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar descrição via OpenAI para produto {ProdutoId}.", produto.Id);
            return ObterDescricaoFallback(produto, itemEstoque);
        }
    }

    private static string ObterDescricaoFallback(Produto produto, ItemEstoque? itemEstoque) =>
        itemEstoque?.DescricaoAnuncio
        ?? produto.SugestaoDescricaoAnuncio
        ?? produto.DescricaoBase
        ?? produto.Nome;

    private static string ConstruirPrompt(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Gere uma descrição profissional em português brasileiro para o produto:");
        sb.AppendLine($"Nome: {produto.Nome}");
        if (!string.IsNullOrWhiteSpace(produto.Marca)) sb.AppendLine($"Marca: {produto.Marca}");
        if (!string.IsNullOrWhiteSpace(produto.DescricaoBase)) sb.AppendLine($"Descrição base: {produto.DescricaoBase}");
        if (variacao != null) sb.AppendLine($"Variação: {variacao.Nome}");
        if (!string.IsNullOrWhiteSpace(instrucoes)) sb.AppendLine($"Instruções: {instrucoes}");
        sb.AppendLine();
        sb.AppendLine("2-3 parágrafos. Apenas a descrição, sem comentários.");
        return sb.ToString();
    }
}
