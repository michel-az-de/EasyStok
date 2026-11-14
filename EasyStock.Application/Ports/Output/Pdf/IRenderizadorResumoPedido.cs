using EasyStock.Application.Services.Notifications.Anexos;

namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>#2544 (S73, D2): desenha o resumo do pedido como imagem PNG para o cliente confirmar pelo WhatsApp.</summary>
public interface IRenderizadorResumoPedido
{
    Task<AnexoGerado> RenderizarAsync(ResumoPedidoDto resumo, CancellationToken ct = default);
}

public sealed record ResumoPedidoDto(
    string NomeLoja,
    string Codigo,
    IReadOnlyList<ResumoPedidoItemDto> Itens,
    decimal Frete,
    decimal Total,
    string Entrega,
    string? Endereco);

public sealed record ResumoPedidoItemDto(string Nome, decimal Quantidade, decimal Subtotal, string? Observacao);
