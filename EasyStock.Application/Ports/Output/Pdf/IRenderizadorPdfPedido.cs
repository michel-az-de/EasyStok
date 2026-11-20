using EasyStock.Application.Services.Notifications.Anexos;

namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>
/// #2962 (E3): PDF do pedido para a cobrança Pix e comprovante "Pago" para o pagamento confirmado. Pensados para a tela do
/// celular: página estreita, fonte grande e alto contraste.
/// </summary>
public interface IRenderizadorPdfPedido
{
    /// <summary>PDF do pedido com o QR Pix e o copia e cola. A chave Pix da loja vai dentro: o arquivo sobe com nome aleatório (#3000).</summary>
    Task<AnexoGerado> RenderizarPedidoAsync(ResumoPedidoDto resumo, PixPdfDto pix, CancellationToken ct = default);

    /// <summary>Comprovante com o selo "Pago", a data e o valor. Não leva o copia e cola.</summary>
    Task<AnexoGerado> RenderizarComprovanteAsync(ResumoPedidoDto resumo, DateTime? pagoEm, CancellationToken ct = default);
}

/// <param name="CopiaCola">Código do Pix. Nunca vai para log nem para texto de falha.</param>
/// <param name="ValorDoPix">Quanto o cliente paga agora (o sinal da encomenda ou o total).</param>
/// <param name="RestanteNaEntrega">Maior que zero na encomenda que paga só o sinal.</param>
/// <param name="Recebedor">Nome do recebedor do Pix, como o app do banco mostra.</param>
public sealed record PixPdfDto(string CopiaCola, decimal ValorDoPix, decimal RestanteNaEntrega, string Recebedor);
