using Amazon.S3;
using Amazon.S3.Model;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Infra.Async.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Async.UnitTests.Storage;

/// <summary>
/// Issue 1285: a mídia recebida do cliente (nota de voz, vídeo, documento) passava pela whitelist do
/// upload público e era recusada; o job só logava e a mensagem ficava sem mídia. O atendimento tem
/// allowlist própria para o upload privado, e o upload público continua tão restrito quanto antes.
/// </summary>
public class MidiaAtendimentoUploadPrivadoTests
{
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();
    private readonly S3CompatibleFileStorage _storage;

    public MidiaAtendimentoUploadPrivadoTests()
    {
        var options = Options.Create(new FileStorageOptions { S3 = new S3StorageOptions { BucketName = "bucket" } });
        _storage = new S3CompatibleFileStorage(options, () => _s3);
    }

    [Theory]
    [InlineData("audio/ogg; codecs=opus", ".ogg")]
    [InlineData("audio/mpeg", ".mp3")]
    [InlineData("audio/mp4", ".m4a")]
    [InlineData("audio/aac", ".aac")]
    [InlineData("audio/amr", ".amr")]
    [InlineData("video/mp4", ".mp4")]
    [InlineData("video/3gpp", ".3gp")]
    [InlineData("application/msword", ".doc")]
    [InlineData("text/plain", ".txt")]
    public async Task MidiaDoClienteEArmazenadaNoStoragePrivado(string mime, string extensao)
    {
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.BaixarMidiaAsync("media-1", Arg.Any<CancellationToken>())
            .Returns(((Stream)new MemoryStream([1, 2, 3]), mime));
        var empresaId = Guid.NewGuid();
        var conversaId = Guid.NewGuid();

        var (chave, _) = await new ArmazenadorMidiaWhatsApp(cloud, _storage)
            .ArmazenarAsync(empresaId, conversaId, "wamid.1", "media-1");

        chave.Should().Be($"atendimento/{empresaId}/{conversaId}/wamid.1{extensao}");
        await _s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.CannedACL != S3CannedACL.PublicRead), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("audio/ogg")]
    [InlineData("video/mp4")]
    [InlineData("text/plain")]
    public async Task UploadPublicoContinuaRecusandoMidiaDoAtendimento(string mime)
    {
        // Mesmo com a allowlist do atendimento anexada, o upload público usa só a whitelist conservadora.
        var act = () => _storage.UploadAsync(new FileUploadRequest(
            "publico", "a.bin", mime, [1, 2, 3], IsPublic: true, ArmazenadorMidiaWhatsApp.MimesPermitidos));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _s3.DidNotReceiveWithAnyArgs().PutObjectAsync(default(PutObjectRequest)!, default);
    }

    [Fact]
    public async Task UploadPrivadoSemAllowlistPropriaContinuaRecusandoAudio()
    {
        var act = () => _storage.UploadAsync(new FileUploadRequest("logs", "a.ogg", "audio/ogg", [1, 2, 3], IsPublic: false));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
