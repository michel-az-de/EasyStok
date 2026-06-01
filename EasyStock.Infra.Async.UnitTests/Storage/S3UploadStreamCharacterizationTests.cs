using Amazon.S3;
using Amazon.S3.Model;
using EasyStock.Infra.Async.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Async.UnitTests.Storage;

/// <summary>
/// Caracteriza o contrato do <c>S3UploadStream</c> (nested em
/// <see cref="S3CompatibleFileStorage"/>): ao ser disposto via <c>await using</c>,
/// o stream faz UM PutObject com a key/contentType corretos e o conteúdo escrito.
///
/// Rede de regressão para #310: verde ANTES (upload síncrono no Dispose) e DEPOIS
/// (upload assíncrono no DisposeAsync) do fix — prova que o contrato não mudou,
/// só deixou de bloquear a thread. (A ausência de bloqueio é verificada
/// estruturalmente, não por este teste — ver plano, refinamento 2.)
/// </summary>
public class S3UploadStreamCharacterizationTests
{
    [Fact]
    public async Task OpenUploadStream_DisposedViaAwaitUsing_UploadsOnceWithWrittenContent()
    {
        // Arrange — captura o conteúdo DURANTE a chamada (refinamento 1): depois do
        // bloco o buffer do MemoryStream já foi liberado / a Position mexida.
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        byte[]? capturado = null;

        var s3 = Substitute.For<IAmazonS3>();
        s3.When(x => x.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()))
          .Do(ci =>
          {
              var req = ci.Arg<PutObjectRequest>();
              using var ms = new MemoryStream();
              req.InputStream.Position = 0;
              req.InputStream.CopyTo(ms);
              capturado = ms.ToArray();
          });

        var options = Options.Create(new FileStorageOptions
        {
            S3 = new S3StorageOptions { BucketName = "test-bucket" },
        });
        var storage = new S3CompatibleFileStorage(options, () => s3);

        // Act
        await using (var upload = await storage.OpenUploadStreamAsync("k", "text/csv"))
        {
            upload.Write(bytes, 0, bytes.Length);
        }

        // Assert
        await s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r =>
                r.Key == "k" && r.ContentType == "text/csv" && r.BucketName == "test-bucket"),
            Arg.Any<CancellationToken>());
        capturado.Should().Equal(bytes);
    }
}
