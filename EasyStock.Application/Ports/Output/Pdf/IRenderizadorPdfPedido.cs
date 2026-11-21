using EasyStock.Application.Services.Notifications.Anexos;

namespace EasyStock.Application.Ports.Output.Pdf;

/// <summary>
/// #2962 (E3): comprovante "Pago" em PDF para o pagamento confirmado. Pensado para a tela do celular: página estreita,
/// fonte grande e alto contraste. O pedido da cobrança Pix sai como imagem do impresso (#3035).
/// </summary>
public interface IRenderizadorPdfPedido
{
    /// <summary>Comprovante com o selo "Pago", a data e o valor. Não leva o copia e cola.</summary>
    Task<AnexoGerado> RenderizarComprovanteAsync(ResumoPedidoDto resumo, DateTime? pagoEm, CancellationToken ct = default);
}

/// <param name="CopiaCola">Código do Pix. Nunca vai para log nem para texto de falha.</param>
/// <param name="ValorDoPix">Quanto o cliente paga agora (o sinal da encomenda ou o total).</param>
/// <param name="RestanteNaEntrega">Maior que zero na encomenda que paga só o sinal.</param>
/// <param name="Recebedor">Nome do recebedor do Pix, como o app do banco mostra.</param>
public sealed record PixPdfDto(string CopiaCola, decimal ValorDoPix, decimal RestanteNaEntrega, string Recebedor);
