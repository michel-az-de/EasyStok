using EasyStock.Api.Services.Impressao;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Services.Impressao;

public class RecursosImpressoTests
{
    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public async Task Snapshot_embute_fontes_e_logo_do_storage_da_empresa(string extensao, string mime)
    {
        var empresa = Guid.NewGuid();
        var loja = Guid.NewGuid();
        var storage = Substitute.For<IFileStorage>();
        var chave = $"lojas/{empresa}/{loja}/logo/exemplo.{extensao}";
        storage.DownloadAsync(chave, Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3 });
        await ComFontes(async ambiente =>
        {
            var html = $"<style>url(\"https://api.test/impressao/fontes/Lora.ttf\") url(\"https://api.test/impressao/fontes/NunitoSans.ttf\")</style><img src=\"https://storage.test/{chave}\">";
            var congelado = await RecursosImpresso.CongelarAsync(html, empresa, "https://api.test", ambiente, storage);
            congelado.Should().Contain("data:font/ttf;base64,AQID").And.Contain($"data:{mime};base64,AQID")
                .And.NotContain("https://api.test").And.NotContain("https://storage.test");
            await storage.Received(1).DownloadAsync(chave, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Logo_externo_ou_de_outra_empresa_nao_e_buscado()
    {
        var storage = Substitute.For<IFileStorage>();
        await ComFontes(async ambiente =>
        {
            foreach (var url in new[] { "http://127.0.0.1/secreto.png", $"https://storage.test/lojas/{Guid.NewGuid()}/{Guid.NewGuid()}/logo/a.png" })
            {
                var executar = () => RecursosImpresso.CongelarAsync($"<img src=\"{url}\">", Guid.NewGuid(), "https://api.test", ambiente, storage);
                await executar.Should().ThrowAsync<UseCaseValidationException>();
            }
            await storage.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    private static async Task ComFontes(Func<IWebHostEnvironment, Task> conferir)
    {
        var pasta = Path.Combine(Path.GetTempPath(), "impressao-fontes-" + Guid.NewGuid());
        var fontes = Path.Combine(pasta, "impressao", "fontes");
        Directory.CreateDirectory(fontes);
        var ambiente = Substitute.For<IWebHostEnvironment>();
        ambiente.WebRootPath.Returns(pasta);
        var arquivos = new[] { Path.Combine(fontes, "Lora.ttf"), Path.Combine(fontes, "NunitoSans.ttf") };
        foreach (var arquivo in arquivos) await File.WriteAllBytesAsync(arquivo, [1, 2, 3]);
        try { await conferir(ambiente); }
        finally
        {
            foreach (var arquivo in arquivos) File.Delete(arquivo);
            Directory.Delete(fontes);
            Directory.Delete(Path.Combine(pasta, "impressao"));
            Directory.Delete(pasta);
        }
    }
}
