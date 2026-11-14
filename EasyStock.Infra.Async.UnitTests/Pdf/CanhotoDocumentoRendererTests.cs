using System.Text;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Infra.Async.Pdf;

namespace EasyStock.Infra.Async.UnitTests.Pdf;

/// <summary>#2211: o canhoto do aviso da equipe sai como PNG (WhatsApp) ou PDF.</summary>
public sealed class CanhotoDocumentoRendererTests
{
    private static readonly byte[] AssinaturaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static CanhotoDto Canhoto(int itens = 2) => new(
        new CanhotoCabecalhoDto("Casa da Baba", "A1B2C3D4", "Maria", "11999990000", "Rua das Flores, 10",
            new DateTime(2026, 11, 4, 12, 0, 0), new DateTime(2026, 11, 4, 9, 30, 0)),
        [new CanhotoGrupoDto("paraServir", "Para servir",
            Enumerable.Range(1, itens).Select(i => new CanhotoItemDto($"Nhoque {i}", "800g", i, "sugo", i == 1 ? "sem sal" : null))
                .ToList())],
        "Tocar a campainha",
        MontarCanhotoUseCase.Rodape);

    [Fact]
    public async Task PngSaiComAssinaturaDeImagem()
    {
        var anexo = await new CanhotoDocumentoRenderer().RenderizarAsync(Canhoto(), FormatoAnexo.Png);

        Assert.Equal("image/png", anexo.ContentType);
        Assert.Equal("png", anexo.Extensao);
        Assert.Equal(AssinaturaPng, anexo.Conteudo.Take(8));
    }

    [Fact]
    public async Task PdfSaiComoDocumento()
    {
        var anexo = await new CanhotoDocumentoRenderer().RenderizarAsync(Canhoto(), FormatoAnexo.Pdf);

        Assert.Equal("application/pdf", anexo.ContentType);
        Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(anexo.Conteudo));
    }

    [Fact]
    public async Task PedidoLongoCabeNumaImagemSo()
    {
        // Altura contínua: o canhoto inteiro numa imagem, sem cortar itens em páginas.
        var curto = await new CanhotoDocumentoRenderer().RenderizarAsync(Canhoto(2), FormatoAnexo.Png);
        var longo = await new CanhotoDocumentoRenderer().RenderizarAsync(Canhoto(40), FormatoAnexo.Png);

        Assert.Equal(AssinaturaPng, longo.Conteudo.Take(8));
        Assert.True(longo.Conteudo.Length > curto.Conteudo.Length);
    }
}
