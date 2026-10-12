using System.Net.Http.Headers;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Ia;

/// <summary>
/// API Messages da Anthropic com ferramentas (S06): <c>POST /v1/messages</c> com os headers
/// <c>x-api-key</c> e <c>anthropic-version: 2023-06-01</c> e corpo <c>{ model, max_tokens, system,
/// messages, tools }</c>. A resposta traz blocos <c>text</c> e <c>tool_use { id, name, input }</c>; a
/// continuação manda <c>tool_result { tool_use_id, content }</c> numa mensagem <c>user</c>. Blocos que o
/// agente não interpreta (ex.: <c>thinking</c>, que o modelo gera por padrão) voltam intactos na
/// continuação. HTTP no mesmo padrão de <c>GeradorAutoPreenchimentoClaude</c>, sem SDK.
/// </summary>
public sealed class AnthropicMessagesClient(
    HttpClient http,
    IOptions<AnthropicAgenteOptions> options,
    ILogger<AnthropicMessagesClient> logger) : IAgenteLlmClient
{
    public const string AnthropicVersion = "2023-06-01";
    private const string Endpoint = "v1/messages";

    private readonly AnthropicAgenteOptions _opcoes = options.Value;

    public bool Disponivel =>
        _opcoes.Enabled && _opcoes.AgenteAtendimentoEnabled && !string.IsNullOrWhiteSpace(_opcoes.ChaveDoAgente);

    public async Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requisicao);
        if (!Disponivel)
            throw new InvalidOperationException("Agente de atendimento desligado (Anthropic:Enabled/ApiKey).");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        if (_opcoes.AutenticacaoBearer)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opcoes.ChaveDoAgente);
        else
            request.Headers.Add("x-api-key", _opcoes.ChaveDoAgente);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new ByteArrayContent(MontarCorpo(requisicao));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await http.SendAsync(request, ct);
        var corpo = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // O corpo de erro da Anthropic não contém a chave; a chave nunca vai para o log.
            logger.LogWarning("Anthropic retornou {StatusCode} para o agente de atendimento: {Erro}",
                (int)response.StatusCode, corpo.Length > 500 ? corpo[..500] : corpo);
            throw new HttpRequestException($"Anthropic respondeu {(int)response.StatusCode}.", null, response.StatusCode);
        }

        return LerResposta(corpo);
    }

    internal byte[] MontarCorpo(RequisicaoLlm requisicao)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("model", _opcoes.ModeloAgente);
            w.WriteNumber("max_tokens", _opcoes.MaxTokensAgente);
            w.WriteString("system", requisicao.System);

            w.WriteStartArray("messages");
            foreach (var mensagem in requisicao.Mensagens)
            {
                w.WriteStartObject();
                w.WriteString("role", mensagem.Papel);
                w.WriteStartArray("content");
                foreach (var bloco in mensagem.Conteudo)
                    EscreverBloco(w, bloco);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            if (requisicao.Ferramentas.Count > 0)
            {
                w.WriteStartArray("tools");
                foreach (var ferramenta in requisicao.Ferramentas)
                {
                    w.WriteStartObject();
                    w.WriteString("name", ferramenta.Nome);
                    w.WriteString("description", ferramenta.Descricao);
                    w.WritePropertyName("input_schema");
                    using (var schema = JsonDocument.Parse(ferramenta.SchemaJson))
                        schema.RootElement.WriteTo(w);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void EscreverBloco(Utf8JsonWriter w, BlocoLlm bloco)
    {
        switch (bloco)
        {
            case BlocoTextoLlm texto:
                w.WriteStartObject();
                w.WriteString("type", "text");
                w.WriteString("text", texto.Texto);
                w.WriteEndObject();
                break;
            case BlocoUsoFerramentaLlm uso:
                w.WriteStartObject();
                w.WriteString("type", "tool_use");
                w.WriteString("id", uso.Id);
                w.WriteString("name", uso.Nome);
                w.WritePropertyName("input");
                uso.Entrada.WriteTo(w);
                w.WriteEndObject();
                break;
            case BlocoResultadoFerramentaLlm resultado:
                w.WriteStartObject();
                w.WriteString("type", "tool_result");
                w.WriteString("tool_use_id", resultado.UsoFerramentaId);
                w.WriteString("content", resultado.Conteudo);
                if (resultado.EhErro) w.WriteBoolean("is_error", true);
                w.WriteEndObject();
                break;
            case BlocoOpacoLlm opaco:
                opaco.Bruto.WriteTo(w);
                break;
            default:
                throw new NotSupportedException($"Bloco não suportado: {bloco.GetType().Name}.");
        }
    }

    private static RespostaLlm LerResposta(string corpo)
    {
        using var doc = JsonDocument.Parse(corpo);
        var raiz = doc.RootElement;

        var blocos = new List<BlocoLlm>();
        if (raiz.TryGetProperty("content", out var conteudo) && conteudo.ValueKind == JsonValueKind.Array)
        {
            foreach (var bloco in conteudo.EnumerateArray())
            {
                var tipo = bloco.TryGetProperty("type", out var t) ? t.GetString() : null;
                blocos.Add(tipo switch
                {
                    "text" => new BlocoTextoLlm(bloco.GetProperty("text").GetString() ?? string.Empty),
                    "tool_use" => new BlocoUsoFerramentaLlm(
                        bloco.GetProperty("id").GetString()!,
                        bloco.GetProperty("name").GetString()!,
                        bloco.GetProperty("input").Clone()),
                    _ => new BlocoOpacoLlm(bloco.Clone())
                });
            }
        }

        var stopReason = raiz.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String ? sr.GetString() : null;
        int entrada = 0, saida = 0;
        if (raiz.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var i) && i.ValueKind == JsonValueKind.Number) entrada = i.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var o) && o.ValueKind == JsonValueKind.Number) saida = o.GetInt32();
        }

        return new RespostaLlm(stopReason, blocos, entrada, saida);
    }
}
