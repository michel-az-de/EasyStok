using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class ArmazenadorMidiaWhatsAppTests
{
    // #1397: tipo fora da lista era recusado sem aviso. Estes são os que a Meta entrega pela Cloud API.
    [Theory]
    [InlineData("image/webp", ".webp")]
    [InlineData("audio/ogg; codecs=opus", ".ogg")]
    [InlineData("audio/mpeg", ".mp3")]
    [InlineData("audio/mp4", ".m4a")]
    [InlineData("audio/amr", ".amr")]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("video/3gpp", ".3gp")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx")]
    [InlineData("text/plain", ".txt")]
    [InlineData("application/zip", ".zip")]
    public async Task AceitaTiposQueAMetaEnvia(string mime, string extensao)
    {
        ArmazenadorMidiaWhatsApp.MimesPermitidos.Should().Contain(mime.Split(';')[0]);

        var cloudClient = Substitute.For<IWhatsAppCloudClient>();
        cloudClient.BaixarMidiaAsync("m", Arg.Any<CancellationToken>()).Returns(((Stream)new MemoryStream([1]), mime));
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult(new StoredFileResult(c.Arg<FileUploadRequest>().FileName, "u", mime, 1)));

        var (chave, _) = await new ArmazenadorMidiaWhatsApp(cloudClient, fileStorage)
            .ArmazenarAsync(Guid.NewGuid(), Guid.NewGuid(), "w", "m");

        chave.Should().Be("w" + extensao);
    }

    [Fact]
    public async Task SalvaComChavePrevisivel()
    {
        var empresaId = Guid.NewGuid();
        var conversaId = Guid.NewGuid();

        var cloudClient = Substitute.For<IWhatsAppCloudClient>();
        cloudClient.BaixarMidiaAsync("media-1", Arg.Any<CancellationToken>())
            .Returns(((Stream)new MemoryStream([1, 2, 3]), "image/jpeg"));

        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = callInfo.Arg<FileUploadRequest>();
                return Task.FromResult(new StoredFileResult(
                    $"{req.BucketPath}/{req.FileName}", "https://storage.test/x", req.ContentType, req.Content.Length));
            });

        var armazenador = new ArmazenadorMidiaWhatsApp(cloudClient, fileStorage);

        var (chave, mime) = await armazenador.ArmazenarAsync(empresaId, conversaId, "wamid.123", "media-1");

        chave.Should().Be($"atendimento/{empresaId}/{conversaId}/wamid.123.jpg");
        mime.Should().Be("image/jpeg");
        await fileStorage.Received(1).UploadAsync(
            Arg.Is<FileUploadRequest>(r =>
                r.BucketPath == $"atendimento/{empresaId}/{conversaId}" &&
                r.FileName == "wamid.123.jpg" &&
                r.ContentType == "image/jpeg" &&
                r.IsPublic == false),
            Arg.Any<CancellationToken>());
    }
}
