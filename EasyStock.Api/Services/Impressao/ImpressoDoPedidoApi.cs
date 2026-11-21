using EasyStock.Application.Ports.Output.Impressao;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Api.Services.Impressao;

/// <summary>
/// #3017 (E3): o impresso <c>cupom-80</c> que a cliente recebe, pronto para o renderizador de PNG. As fontes vão como
/// arquivos junto do HTML (nome relativo), então o Chromium não depende de a Api estar alcançável pela rede interna.
/// </summary>
public sealed class ImpressoDoPedidoApi(
    MontarPedidoImpressoUseCase montar, PedidoImpressoHtml html, IWebHostEnvironment ambiente, IConfiguration configuracao)
    : IImpressoDoPedido
{
    private static readonly string[] Fontes = ["Lora.ttf", "NunitoSans.ttf"];

    public async Task<DocumentoHtml?> ObterComPixAsync(Guid empresaId, Guid pedidoId, PixPdfDto pix, CancellationToken ct = default)
    {
        var pedido = await montar.ExecuteAsync(new MontarPedidoImpressoInput(empresaId, pedidoId), ct);
        if (pedido is null) return null;
        var baseUrl = configuracao["App:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var pagina = await html.RenderizarAsync(
            pedido, PedidoImpressoHtml.Pedido, ImpressoHtml.Cupom80, baseUrl, ct: ct, fontesRelativas: true);
        var recursos = new List<RecursoHtml>(Fontes.Length);
        foreach (var fonte in Fontes)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(ambiente.WebRootPath, "impressao", "fontes", fonte), ct);
            recursos.Add(new RecursoHtml(fonte, "font/ttf", bytes));
        }
        return new DocumentoHtml(pagina, recursos);
    }
}
