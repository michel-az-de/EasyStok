using EasyStock.Application.UseCases.Etiquetas.Folha;
using EasyStock.Infra.Async.Pdf;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Pdf;

/// <summary>#2200, banca de DoS: o render é síncrono e pesado; só <see cref="FolhaEtiquetasPdfRenderer.RendersSimultaneosMax"/> rodam ao mesmo tempo.</summary>
public sealed class FolhaEtiquetasPdfRendererConcorrenciaTests
{
    private static readonly PlanoFolhaEtiquetas PlanoVazio = new(210m, 297m, false, [new PaginaPlano([])]);

    [Fact]
    public async Task RenderAsync_ComTodasAsVagasOcupadas_EsperaECancelaComOToken()
    {
        for (var i = 0; i < FolhaEtiquetasPdfRenderer.RendersSimultaneosMax; i++)
            await FolhaEtiquetasPdfRenderer.Vagas.WaitAsync();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var render = () => new FolhaEtiquetasPdfRenderer().RenderAsync(PlanoVazio, cts.Token);

            await render.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            FolhaEtiquetasPdfRenderer.Vagas.Release(FolhaEtiquetasPdfRenderer.RendersSimultaneosMax);
        }
    }

    [Fact]
    public async Task RenderAsync_ComVagaLivre_Renderiza()
    {
        var pdf = await new FolhaEtiquetasPdfRenderer().RenderAsync(PlanoVazio);

        pdf.Should().NotBeEmpty();
    }
}
