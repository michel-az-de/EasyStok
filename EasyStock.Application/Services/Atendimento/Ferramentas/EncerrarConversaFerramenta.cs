using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary><c>encerrar_conversa()</c>: <c>Conversa.Encerrar()</c>. A próxima mensagem do contato abre outra conversa.</summary>
public sealed class EncerrarConversaFerramenta : IFerramentaAgente
{
    public string Nome => "encerrar_conversa";

    public string Descricao =>
        "Encerra a conversa quando o cliente se despediu e não há nada pendente. Sua resposta final ainda é enviada.";

    public string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        contexto.Conversa.Encerrar(contexto.Agora);
        return Task.FromResult(FerramentaJson.Serializar(new { encerrada = true }));
    }
}
