using System.Text.Json;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary><c>encerrar_conversa()</c>: <c>Conversa.Encerrar()</c>. A próxima mensagem do contato abre outra conversa.</summary>
public sealed class EncerrarConversaFerramenta : IFerramentaAgente
{
    public string Nome => "encerrar_conversa";

    public string Descricao =>
        "Encerra a conversa quando o cliente se despediu e não há pedido vinculado. Com pedido vinculado, encaminhe para a equipe. Envia uma despedida fixa de encerramento.";

    public string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (contexto.Conversa.PedidoEmAndamentoId is not null)
            return Task.FromResult(FerramentaJson.Serializar(new { erro = "pedido_vinculado_requer_atendimento" }));

        contexto.Conversa.Encerrar(contexto.Agora);
        contexto.RegistrarRespostaVerificada("Conversa encerrada. Quando precisar, mande uma nova mensagem por aqui.");
        return Task.FromResult(FerramentaJson.Serializar(new { encerrada = true }));
    }
}
