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
/// rastreada do turno: o commit é do serviço, no fim.
/// </summary>
public sealed class ContextoTurnoAgente(Guid empresaId, Conversa conversa, DateTime agora)
{
    public Guid EmpresaId { get; } = empresaId;
    public Conversa Conversa { get; } = conversa;
    public DateTime Agora { get; } = agora;
}
