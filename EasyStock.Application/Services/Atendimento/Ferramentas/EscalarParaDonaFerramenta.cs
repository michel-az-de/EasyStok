using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary><c>escalar_para_dona(motivo)</c>: a conversa sai do automático e a dona é avisada (aviso completo na S07).</summary>
public sealed class EscalarParaDonaFerramenta(IEscaladorConversa escalador) : IFerramentaAgente
{
    public string Nome => "escalar_para_dona";

    public string Descricao =>
        "Passa a conversa para a dona. Depois disso você não responde mais nesta conversa; avise o cliente " +
        "com uma frase curta que a Baba vai continuar o atendimento.";

    public string SchemaJson =>
        """{"type":"object","properties":{"motivo":{"type":"string","description":"Por que a dona precisa entrar, em uma frase"}},"required":["motivo"],"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var motivo = FerramentaJson.LerTexto(entrada, "motivo") ?? "pedido do agente";
        await escalador.EscalarAsync(contexto.EmpresaId, contexto.Conversa, motivo, contexto.Agora, ct);
        return FerramentaJson.Serializar(new { escalado = true });
    }
}
