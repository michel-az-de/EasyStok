using EasyStock.Application.Ports.Output.Impressao;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #3017 (E3): o balão 1 da cobrança Pix é o PNG do próprio impresso que a cliente recebe na entrega. Sobe com nome
/// aleatório, como o canhoto (#2544), para o WhatsApp baixar pela URL pública.
/// </summary>
public sealed class PublicadorImpressoPedido(
    IImpressoDoPedido impressos, IRenderizadorHtml renderizador, IFileStorage armazenamento, IConfiguration configuration)
{
    /// <summary>Largura da bobina 80 mm, o papel do impresso que a cliente recebe.</summary>
    public const int LarguraPapelMm = 80;

    /// <summary>URL pública do PNG; nulo quando o pedido não é da empresa.</summary>
    public async Task<string?> PublicarPixAsync(Guid empresaId, Guid pedidoId, PixPdfDto pix, CancellationToken ct)
    {
        var documento = await impressos.ObterComPixAsync(empresaId, pedidoId, pix, ct);
        if (documento is null) return null;
        var png = await renderizador.RenderizarPngAsync(documento, LarguraPapelMm, ct);
        var salvo = await armazenamento.UploadAsync(new FileUploadRequest(
            $"atendimento/impressos/{empresaId:N}", $"{Guid.NewGuid():D}.png", png.ContentType, png.Conteudo), ct);
        return MidiaPublica.Url(salvo.Url, configuration)
            ?? throw new InvalidOperationException("O impresso não ficou em link HTTPS público.");
    }
}
