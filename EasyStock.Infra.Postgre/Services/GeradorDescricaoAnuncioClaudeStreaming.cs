using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace EasyStock.Infra.Postgre.Services
{
    internal sealed class GeradorDescricaoAnuncioClaudeStreaming(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeradorDescricaoAnuncioClaudeStreaming> logger) : IGeradorDescricaoAnuncioStreaming
    {
        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string ModelId = "claude-haiku-4-5-20251001";

        public async IAsyncEnumerable<string> GerarStreamAsync(
            Produto produto,
            ProdutoVariacao? variacao,
            ItemEstoque? itemEstoque,
            string? instrucoesComplementares = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var apiKey = configuration["Anthropic:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning("Anthropic:ApiKey não configurado. Retornando fallback para produto {ProdutoId}.", produto.Id);
                yield return itemEstoque?.DescricaoAnuncio ?? produto.SugestaoDescricaoAnuncio ?? produto.DescricaoBase ?? produto.Nome;
                yield break;
            }

            var prompt = ConstruirPrompt(produto, variacao, itemEstoque, instrucoesComplementares);

            var response = await TentarIniciarStreamAsync(apiKey, prompt, produto.Id, ct);

            if (response == null)
            {
                yield return itemEstoque?.DescricaoAnuncio ?? produto.SugestaoDescricaoAnuncio ?? produto.DescricaoBase ?? produto.Nome;
                yield break;
            }

            using (response)
            {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);

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
                    logger.LogDebug(ex, "Falha ao parsear chunk SSE: {Data}", data);
                }

                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
            }
        }

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

        private async Task<HttpResponseMessage?> TentarIniciarStreamAsync(string apiKey, string prompt, Guid produtoId, CancellationToken ct)
        {
            try
            {
                var client = httpClientFactory.CreateClient("Anthropic");

                var requestBody = new
                {
                    model = ModelId,
                    max_tokens = 1024,
                    stream = true,
                    messages = new[] { new { role = "user", content = prompt } }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Content = JsonContent.Create(requestBody);

                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao iniciar stream Anthropic para produto {ProdutoId}.", produtoId);
                return null;
            }
        }
    }
}

