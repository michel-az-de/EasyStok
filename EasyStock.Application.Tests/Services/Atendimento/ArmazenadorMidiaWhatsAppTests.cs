using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class ArmazenadorMidiaWhatsAppTests
{
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
