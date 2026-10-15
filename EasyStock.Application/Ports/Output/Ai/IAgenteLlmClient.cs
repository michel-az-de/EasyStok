using System.Text.Json;

namespace EasyStock.Application.Ports.Output.Ai;

/// <summary>
/// Chamada ao LLM com ferramentas usada pelo agente de atendimento (S06). A implementação real
/// fala com a API Messages da Anthropic (<c>AnthropicMessagesClient</c>); o modelo, a chave e o
/// liga/desliga vêm de configuração (<c>Anthropic:*</c>), nunca do chamador.
/// </summary>
public interface IAgenteLlmClient
{
    /// <summary>
    /// Falso quando <c>Anthropic:Enabled=false</c> ou sem <c>Anthropic:ApiKey</c>: o turno não chama o
    /// LLM e o webhook segue só persistindo mensagens (rollback da S06).
    /// </summary>
    bool Disponivel { get; }

    Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default);
}

public sealed record RequisicaoLlm(
    string System,
    IReadOnlyList<MensagemLlm> Mensagens,
    IReadOnlyList<FerramentaLlm> Ferramentas);

/// <summary><paramref name="Papel"/> é <c>user</c> ou <c>assistant</c>.</summary>
public sealed record MensagemLlm(string Papel, IReadOnlyList<BlocoLlm> Conteudo)
{
    public const string Usuario = "user";
    public const string Assistente = "assistant";
}

/// <summary><paramref name="SchemaJson"/> é o JSON Schema do <c>input_schema</c>.</summary>
public sealed record FerramentaLlm(string Nome, string Descricao, string SchemaJson);

public abstract record BlocoLlm;

public sealed record BlocoTextoLlm(string Texto) : BlocoLlm;

public sealed record BlocoUsoFerramentaLlm(string Id, string Nome, JsonElement Entrada) : BlocoLlm;

public sealed record BlocoResultadoFerramentaLlm(string UsoFerramentaId, string Conteudo, bool EhErro = false) : BlocoLlm;

/// <summary>
/// Bloco que o agente não interpreta (ex.: <c>thinking</c>) e precisa voltar inalterado na
/// continuação do mesmo turno.
/// </summary>
public sealed record BlocoOpacoLlm(JsonElement Bruto) : BlocoLlm;

public sealed record RespostaLlm(
    string? StopReason,
    IReadOnlyList<BlocoLlm> Conteudo,
    int TokensEntrada,
    int TokensSaida)
{
    public const string StopToolUse = "tool_use";
}
