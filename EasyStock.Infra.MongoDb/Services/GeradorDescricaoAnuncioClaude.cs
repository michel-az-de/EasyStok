using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.MongoDb.Services;

internal sealed class GeradorDescricaoAnuncioClaude(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GeradorDescricaoAnuncioClaude> logger) : IGeradorDescricaoAnuncio
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ModelId = "claude-haiku-4-5-20251001";

    public async Task<string> GerarAsync(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoesComplementares = null)
    {
        var apiKey = configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("Anthropic:ApiKey não configurado. Usando fallback.");
            return itemEstoque?.DescricaoAnuncio ?? produto.SugestaoDescricaoAnuncio ?? produto.DescricaoBase ?? produto.Nome;
        }

        try
        {
            var prompt = BuildPrompt(produto, variacao, itemEstoque, instrucoesComplementares);
            var client = httpClientFactory.CreateClient("Anthropic");

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = JsonContent.Create(new
            {
                model = ModelId,
                max_tokens = 1024,
                messages = new[] { new { role = "user", content = prompt } }
            });

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("content")[0].GetProperty("text").GetString() ?? produto.Nome;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar descricao no Mongo infrastructure.");
            return itemEstoque?.DescricaoAnuncio ?? produto.SugestaoDescricaoAnuncio ?? produto.DescricaoBase ?? produto.Nome;
        }
    }

    private static string BuildPrompt(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Gere uma descricao de anuncio profissional para marketplace.");
        builder.AppendLine($"Nome: {produto.Nome}");

        if (!string.IsNullOrWhiteSpace(produto.Marca))
            builder.AppendLine($"Marca: {produto.Marca}");

        if (!string.IsNullOrWhiteSpace(produto.DescricaoBase))
            builder.AppendLine($"Descricao base: {produto.DescricaoBase}");

        if (variacao is not null)
        {
            builder.AppendLine($"Variacao: {variacao.Nome}");
            if (!string.IsNullOrWhiteSpace(variacao.Cor)) builder.AppendLine($"Cor: {variacao.Cor}");
            if (!string.IsNullOrWhiteSpace(variacao.Tamanho)) builder.AppendLine($"Tamanho: {variacao.Tamanho}");
        }

        if (itemEstoque is not null)
        {
            if (!string.IsNullOrWhiteSpace(itemEstoque.Cor)) builder.AppendLine($"Cor estoque: {itemEstoque.Cor}");
            if (!string.IsNullOrWhiteSpace(itemEstoque.Tamanho)) builder.AppendLine($"Tamanho estoque: {itemEstoque.Tamanho}");
        }

        if (!string.IsNullOrWhiteSpace(instrucoes))
            builder.AppendLine($"Instrucoes: {instrucoes}");

        builder.AppendLine("Responda apenas com a descricao final.");
        return builder.ToString();
    }
}
