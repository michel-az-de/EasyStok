using System.Text.Json;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// Ferramenta que o agente de atendimento (S06) pode chamar. Executa contra a Application, nunca
/// contra o banco direto, e devolve o texto do <c>tool_result</c> (JSON curto ou frase).
/// </summary>
public interface IFerramentaAgente
{
    string Nome { get; }
    string Descricao { get; }

    /// <summary>JSON Schema do <c>input_schema</c>.</summary>
    string SchemaJson { get; }

    Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default);
}

/// <summary>
/// Estado do turno que as ferramentas leem e alteram. A <see cref="Conversa"/> é a instância
/// rastreada do turno: o commit é do serviço, no fim, salvo a saída enviada pela ferramenta, que
/// <see cref="GravarSaidaAsync"/> grava na hora (#1791).
/// </summary>
public sealed class ContextoTurnoAgente(Guid empresaId, Conversa conversa, DateTime agora,
    Func<CancellationToken, Task>? gravarSaida = null)
{
    public Guid EmpresaId { get; } = empresaId;
    public Conversa Conversa { get; } = conversa;
    public DateTime Agora { get; } = agora;
    public string? RespostaVerificada { get; private set; }
    public bool EnvioVerificado { get; private set; }
    public bool HouveEnvio { get; private set; }

    public void RegistrarRespostaVerificada(string texto) => RespostaVerificada = texto;

    public void RegistrarEnvioVerificado() => EnvioVerificado = HouveEnvio = true;

    /// <summary>
    /// Grava já a <see cref="Mensagem"/> que a ferramenta acabou de enviar e adicionar (#1791). O status e o eco da
    /// Meta chegam enquanto o LLM ainda responde e procuram a mensagem pelo wamid: sem ela no banco, o status se
    /// perde (sem reenvio S57 nem SMS S60) e o eco vira mensagem da dona, que assume a conversa.
    /// </summary>
    public Task GravarSaidaAsync(CancellationToken ct = default) => gravarSaida?.Invoke(ct) ?? Task.CompletedTask;

    public void LimparRespostaVerificada()
    {
        RespostaVerificada = null;
        EnvioVerificado = false;
    }
}
