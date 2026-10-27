using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

/// <summary>
/// <c>confirmar_encomenda(pedido_id?)</c> (#2096): o cliente confirmou na conversa a encomenda com data marcada. Só pedido
/// do cliente da conversa (o id vem do LLM); sem id, o pedido em andamento. A confirmação tira a encomenda do aviso
/// "sem confirmação". O commit é do turno do agente.
/// </summary>
public sealed class ConfirmarEncomendaFerramenta(IPedidoStorefrontRepository pedidos) : IFerramentaAgente
{
    public string Nome => "confirmar_encomenda";

    public string Descricao =>
        "Registra que o cliente confirmou a encomenda com data marcada (ele disse que mantém o pedido, a data e o horário). " +
        "Use só depois de o cliente confirmar claramente nesta conversa.";

    public string SchemaJson =>
        """{"type":"object","properties":{"pedido_id":{"type":"string","description":"Id do pedido, se houver mais de um"}},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        var conversa = contexto.Conversa;
        if (conversa.ClienteId is not { } clienteId)
            return FerramentaJson.Serializar(new { erro = "cliente_sem_pedidos" });

        var pedidoId = Guid.TryParse(FerramentaJson.LerTexto(entrada, "pedido_id"), out var informado)
            ? informado
            : conversa.PedidoEmAndamentoId;
        if (pedidoId is null) return FerramentaJson.Serializar(new { erro = "pedido_nao_informado" });

        var pedido = await pedidos.GetForUpdateAsync(contexto.EmpresaId, pedidoId.Value, ct);
        if (pedido is null || pedido.EmpresaId != contexto.EmpresaId || pedido.ClienteId != clienteId)
            return FerramentaJson.Serializar(new { erro = "pedido_nao_encontrado" });
        if (pedido.AgendadoParaEm is null || pedido.EstaFinalizado)
            return FerramentaJson.Serializar(new { erro = "pedido_sem_encomenda_aberta" });

        if (pedido.ConfirmarPeloCliente(contexto.Agora)) await pedidos.UpdateAsync(pedido, ct);
        return FerramentaJson.Serializar(new { situacao = "confirmada" });
    }
}
