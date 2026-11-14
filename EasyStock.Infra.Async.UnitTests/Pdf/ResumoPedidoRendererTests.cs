using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Infra.Async.Pdf;

namespace EasyStock.Infra.Async.UnitTests.Pdf;

/// <summary>#2544 (S73, D2): o resumo do pedido para o cliente confirmar sai como PNG, numa imagem só.</summary>
public sealed class ResumoPedidoRendererTests
{
    private static readonly byte[] AssinaturaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ResumoPedidoDto Resumo(int itens = 2) => new(
        "Casa da Massa", "A1B2C3",
        Enumerable.Range(1, itens).Select(i => new ResumoPedidoItemDto($"Nhoque {i}", i, 30m * i, i == 1 ? "sem sal" : null)).ToList(),
        8m, 98m, "15/11/2026", "Rua das Flores, 10");

    [Fact]
    public async Task SaiComoPng()
    {
        var anexo = await new ResumoPedidoRenderer().RenderizarAsync(Resumo());

        Assert.Equal("image/png", anexo.ContentType);
        Assert.Equal("png", anexo.Extensao);
        Assert.Equal(AssinaturaPng, anexo.Conteudo.Take(8));
    }

    [Fact]
    public async Task PedidoLongoCabeNumaImagemSo()
    {
        var curto = await new ResumoPedidoRenderer().RenderizarAsync(Resumo(2));
        var longo = await new ResumoPedidoRenderer().RenderizarAsync(Resumo(30));

        Assert.Equal(AssinaturaPng, longo.Conteudo.Take(8));
        Assert.True(longo.Conteudo.Length > curto.Conteudo.Length);
    }
}
