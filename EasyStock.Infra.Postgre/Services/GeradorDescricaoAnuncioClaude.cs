using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyStock.Infra.Postgre.Services
{
    internal sealed class GeradorDescricaoAnuncioClaude(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeradorDescricaoAnuncioClaude> logger) : IGeradorDescricaoAnuncio
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string ModelId = "claude-haiku-4-5-20251001";
        private const string AnthropicApiVersion = "2023-06-01";
        private const int MaxTokens = 1024;

        public async Task<string> GerarAsync(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoesComplementares = null)
        {
            var apiKey = configuration["Anthropic:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning("Anthropic:ApiKey não configurado. Usando descrição existente como fallback.");
                return ObterDescricaoFallback(produto, itemEstoque);
            }

            try
            {
                var prompt = ConstruirPrompt(produto, variacao, itemEstoque, instrucoesComplementares);
                var client = httpClientFactory.CreateClient("Anthropic");

                var requestBody = new
                {
                    model = ModelId,
                    max_tokens = MaxTokens,
                    messages = new[] { new { role = "user", content = prompt } }
                };

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                httpRequest.Headers.Add("x-api-key", apiKey);
                httpRequest.Headers.Add("anthropic-version", AnthropicApiVersion);
                httpRequest.Content = JsonContent.Create(requestBody);

                var response = await client.SendAsync(httpRequest);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    var logLevel = (int)response.StatusCode >= 500 ? Microsoft.Extensions.Logging.LogLevel.Error : Microsoft.Extensions.Logging.LogLevel.Warning;
                    logger.Log(logLevel, "Anthropic API retornou {StatusCode} para produto {ProdutoId}: {ErrorBody}",
                        (int)response.StatusCode, produto.Id, errorBody);
                    return ObterDescricaoFallback(produto, itemEstoque);
                }

                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                var descricao = json
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString();

                logger.LogInformation("Descricao de anuncio gerada via Anthropic para produto {ProdutoId}.", produto.Id);
                return descricao ?? produto.Nome;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao gerar descricao via Anthropic API. Usando fallback para produto {ProdutoId}.", produto.Id);
                return ObterDescricaoFallback(produto, itemEstoque);
            }
        }

        /// <summary>
        /// Retorna a melhor descrição disponível localmente, sem chamar a API.
        /// Prioridade: DescricaoAnuncio do item → SugestaoDescricaoAnuncio → DescricaoBase → Nome do produto.
        /// </summary>
        private static string ObterDescricaoFallback(Produto produto, ItemEstoque? itemEstoque) =>
            itemEstoque?.DescricaoAnuncio
            ?? produto.SugestaoDescricaoAnuncio
            ?? produto.DescricaoBase
            ?? produto.Nome;

        private static string ConstruirPrompt(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Gere uma descricao de anuncio atraente e profissional para o seguinte produto de e-commerce:");
            sb.AppendLine($"Nome: {produto.Nome}");

            if (!string.IsNullOrWhiteSpace(produto.Marca))
                sb.AppendLine($"Marca: {produto.Marca}");

            if (!string.IsNullOrWhiteSpace(produto.DescricaoBase))
                sb.AppendLine($"Descricao base: {produto.DescricaoBase}");

            if (variacao != null)
            {
                sb.AppendLine($"Variacao: {variacao.Nome}");
                if (!string.IsNullOrWhiteSpace(variacao.Cor)) sb.AppendLine($"Cor: {variacao.Cor}");
                if (!string.IsNullOrWhiteSpace(variacao.Tamanho)) sb.AppendLine($"Tamanho: {variacao.Tamanho}");
                if (!string.IsNullOrWhiteSpace(variacao.DescricaoComercial)) sb.AppendLine($"Descricao comercial: {variacao.DescricaoComercial}");
            }

            if (itemEstoque != null)
            {
                if (!string.IsNullOrWhiteSpace(itemEstoque.Cor)) sb.AppendLine($"Cor (estoque): {itemEstoque.Cor}");
                if (!string.IsNullOrWhiteSpace(itemEstoque.Tamanho)) sb.AppendLine($"Tamanho (estoque): {itemEstoque.Tamanho}");
            }

            if (!string.IsNullOrWhiteSpace(instrucoes))
                sb.AppendLine($"Instrucoes adicionais: {instrucoes}");

            sb.AppendLine();
            sb.AppendLine("Escreva uma descricao de 2-3 paragrafos destacando os beneficios principais, adequada para marketplace. Responda apenas com a descricao, sem comentarios adicionais.");

            return sb.ToString();
        }
    }
}
