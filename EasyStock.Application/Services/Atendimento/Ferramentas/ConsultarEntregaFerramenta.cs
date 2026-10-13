using System.Text.Json;
using EasyStock.Application.UseCases.Atendimento.Entregas;

namespace EasyStock.Application.Services.Atendimento.Ferramentas;

// O modelo não escolhe IDs: rastreio limitado ao pedido e cliente da conversa.
public sealed class ConsultarEntregaFerramenta(IPedidoRepository pedidos, LogisticaUseCases logistica) : IFerramentaAgente
{
    public string Nome => "consultar_entrega";
    public string Descricao => "Consulta o acompanhamento registrado da entrega do pedido desta conversa. Não contrata, não paga e não inventa localização. Dados de demonstração são identificados.";
    public string SchemaJson => """{"type":"object","properties":{},"additionalProperties":false}""";

    public async Task<string> ExecutarAsync(ContextoTurnoAgente contexto, JsonElement entrada, CancellationToken ct = default)
    {
        if (contexto.Conversa.ClienteId is not { } clienteId || contexto.Conversa.PedidoEmAndamentoId is not { } pedidoId)
            return FerramentaJson.Serializar(new { erro = "conversa_sem_pedido" });
        var pedido = await pedidos.GetByIdWithDetailsAsync(contexto.EmpresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != contexto.EmpresaId || pedido.ClienteId != clienteId)
            return FerramentaJson.Serializar(new { erro = "pedido_nao_encontrado" });
        try
        {
            var dados = await logistica.ContextoAsync(contexto.EmpresaId, null, pedidoId, ct);
            var despacho = dados.Despachos.OrderByDescending(d => d.CriadoEm).FirstOrDefault();
            if (despacho is null)
                return FerramentaJson.Serializar(new { situacao = "sem_transporte_registrado", mensagem = "A equipe ainda não registrou o transporte deste pedido." });
            var situacao = despacho.Status switch
            {
                "agendado" => "aguardando coleta", "a_caminho" => "a caminho", "entregue" => "entregue",
                "devolvendo" => "retornando à loja", "devolvido" => "devolvido à loja", "cancelado" => "cancelado",
                _ => "precisa de conferência pela equipe"
            };
            var texto = $"Demonstração: a entrega deste pedido está {situacao}. Este acompanhamento usa eventos simulados.";
            contexto.RegistrarRespostaVerificada(texto);
            return FerramentaJson.Serializar(new { despacho.Status, despacho.Simulado, despacho.RastreioUrl, mensagem = texto });
        }
        catch (UseCaseValidationException)
        {
            return FerramentaJson.Serializar(new { situacao = "acompanhamento_indisponivel", mensagem = "A equipe pode conferir o transporte do seu pedido." });
        }
    }
}
