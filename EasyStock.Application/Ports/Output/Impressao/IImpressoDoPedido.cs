using EasyStock.Application.Ports.Output.Pdf;

namespace EasyStock.Application.Ports.Output.Impressao;

/// <summary>
/// #3017 (E3): o impresso que a cliente recebe na entrega, com o bloco Pix da cobrança. Quem monta o HTML é a Api,
/// que é dona do layout; a Application só pede o documento pronto.
/// </summary>
public interface IImpressoDoPedido
{
    /// <summary>Nulo quando o pedido não é da empresa.</summary>
    Task<DocumentoHtml?> ObterComPixAsync(Guid empresaId, Guid pedidoId, PixPdfDto pix, CancellationToken ct = default);
}
