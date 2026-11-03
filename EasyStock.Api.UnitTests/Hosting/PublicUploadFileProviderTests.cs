using EasyStock.Api.Hosting;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;

namespace EasyStock.Api.UnitTests.Hosting;

public sealed class PublicUploadFileProviderTests
{
    [Theory]
    [InlineData("atendimento/empresa/conversa/wpp-ABC123.ogg", false)]
    [InlineData("atendimento//empresa/conversa/wpp-ABC123.jpg", false)]
    [InlineData("atendimento/empresa/conversa/wamid.ABC.jpg", false)]
    [InlineData("atendimento/empresa/conversa/b6970f4111cd4805a71cdd6ea7ab8a03.pdf", false)]
    [InlineData("atendimento/empresa/conversa/b6970f41-11cd-4805-a71c-dd6ea7ab8a03.ogg", true)]
    [InlineData("atendimento/empresa/conversa/7baba59f-cad2-480a-859c-656d502d517e.jpg", true)]
    [InlineData("acervo//empresa/foto.jpg", false)]
    [InlineData("produtos/empresa/foto.jpg", true)]
    [InlineData("clientes/empresa/cliente/foto.jpg", false)]
    [InlineData("clientes//empresa/cliente/foto.jpg", false)]
    public void SomenteMidiaGeradaParaEnvioFicaPublicaNoAtendimento(string subpath, bool publica)
    {
        var root = Path.Combine(Path.GetTempPath(), "easystok-public-upload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, subpath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
            var tipo = typeof(PipelineExtensions).Assembly.GetType("EasyStock.Api.Hosting.PublicUploadFileProvider")!;
            var provider = (IFileProvider)Activator.CreateInstance(tipo, root)!;

            provider.GetFileInfo(subpath).Exists.Should().Be(publica);
            provider.GetDirectoryContents("atendimento").Exists.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
