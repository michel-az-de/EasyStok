using System.Text.Json;

namespace EasyStock.Application.Ports.Output.Ai;

/// <summary>
/// Chamada ao LLM com ferramentas usada pelo agente de atendimento (S06). A implementação real
/// fala com a API Messages da Anthropic (<c>AnthropicMessagesClient</c>); o modelo, a chave e o
/// liga/desliga vêm de configuração (<c>Anthropic:*</c>). Exceção (S68, ADR-0066): o turno pode pedir
/// <see cref="RequisicaoLlm.Modelo"/>, sempre um da lista aprovada; nulo usa o configurado.
/// </summary>
public interface IAgenteLlmClient
{
    /// <summary>
    /// Falso quando <c>Anthropic:Enabled=false</c> ou sem <c>Anthropic:ApiKey</c>: o turno não chama o
    /// LLM e o webhook segue só persistindo mensagens (rollback da S06).
    /// </summary>
    bool Disponivel { get; }

    /// <summary>
    /// #3064 (S2b de #2948): disponibilidade para a loja. Com a flag <c>ia.provedor_por_loja</c>, o roteador olha o
    /// provedor, a chave e a cota da loja; sem ela, vale <see cref="Disponivel"/>. Decoradores encaminham ao interno.
    /// </summary>
    Task<bool> DisponivelAsync(Guid empresaId, CancellationToken ct = default) => Task.FromResult(Disponivel);

    Task<RespostaLlm> EnviarAsync(RequisicaoLlm requisicao, CancellationToken ct = default);
}

/// <summary>
/// <paramref name="System"/> é a parte estável do prompt (a que o provedor pode guardar em cache). <paramref name="SystemVolatil"/>
/// é o que muda a cada turno (data, hora, dossiê): vai num bloco à parte, depois do breakpoint de cache. Nulo ou vazio
/// mantém o corpo de antes, com <c>system</c> como texto único.
/// </summary>
public sealed record RequisicaoLlm(
    string System,
    IReadOnlyList<MensagemLlm> Mensagens,
    IReadOnlyList<FerramentaLlm> Ferramentas,
    string? Modelo = null,
    string? SystemVolatil = null)
{
    /// <summary>Modelo do agente quando a configuração não diz outro (<c>Anthropic:ModeloAgente</c> ausente).</summary>
    public const string ModeloPadrao = "claude-sonnet-5";

    /// <summary>#3063: loja que paga a chamada. Vazio não grava <c>chamada_ia</c> (só testes antigos).</summary>
    public Guid EmpresaId { get; init; }

    /// <summary>
    /// #3273 (R1): piso do teto de saída desta chamada, para a nova tentativa depois de <c>max_tokens</c>. Nulo usa só o teto
    /// configurado; o cliente nunca baixa o configurado por causa dele.
    /// </summary>
    public int? MaxTokensMinimo { get; init; }

    /// <summary>
    /// #3287 (R5): turno do agente que fez a chamada, para ligar <c>chamada_ia</c> ao <c>TurnoAgente</c>. Nulo fora do turno.
    /// Referência fraca, sem FK: <c>chamada_ia</c> grava em escopo próprio e antes do turno. Se o turno cai e é descartado
    /// (exceção no envio, cancelamento), o id fica sem <c>TurnoAgente</c> correspondente.
    /// </summary>
    public Guid? TurnoAgenteId { get; init; }

    /// <summary>#3063: para que a chamada serve, no registro de uso.</summary>
    public EasyStock.Domain.Enums.Ia.FinalidadeIa Finalidade { get; init; } = EasyStock.Domain.Enums.Ia.FinalidadeIa.Outra;
}

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

/// <summary>
/// S67 (#2534): <paramref name="TokensCacheLido"/> é o prompt lido do cache do provedor (preço próprio, fora de
/// <paramref name="TokensEntrada"/>); <paramref name="Modelo"/> é o modelo que respondeu, nulo quando o cliente não sabe.
/// </summary>
public sealed record RespostaLlm(
    string? StopReason,
    IReadOnlyList<BlocoLlm> Conteudo,
    int TokensEntrada,
    int TokensSaida,
    int TokensCacheLido = 0,
    string? Modelo = null,
    int TokensCacheEscrito = 0)
{
    public const string StopToolUse = "tool_use";

    /// <summary>#3273 (R1): o modelo parou porque o teto de saída acabou; o texto, se houver, está cortado.</summary>
    public const string StopMaxTokens = "max_tokens";
}
