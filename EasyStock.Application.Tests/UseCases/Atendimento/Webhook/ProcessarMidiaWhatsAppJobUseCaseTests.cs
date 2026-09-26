using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Webhook;

public class ProcessarMidiaWhatsAppJobUseCaseTests
{
    [Fact]
    public async Task DrenaJobEAnexaMidiaNaMensagem()
    {
        var empresaId = Guid.NewGuid();
        var conversaId = Guid.NewGuid();
        var wamid = "wamid.imagem1";

        var mensagem = Domain.Entities.Atendimento.Mensagem.Entrada(
            empresaId, conversaId, DateTime.UtcNow, TipoConteudoMensagem.Imagem, externoId: wamid);

        var conversaRepository = Substitute.For<IConversaRepository>();
        conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, wamid, Arg.Any<CancellationToken>())
            .Returns(mensagem);

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

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var armazenador = new ArmazenadorMidiaWhatsApp(cloudClient, fileStorage);
        var processador = new ProcessarMidiaWhatsAppJobUseCase(
            conversaRepository, armazenador, unitOfWork, NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance);

        // O enfileiramento em si já é coberto por ProcessarEventoWhatsAppUseCaseTests.ImagemArmazena;
        // aqui prova-se que DRENAR o job (o que o AtendimentoFilaMidiaBackgroundService faz em
        // produção) efetivamente baixa e anexa a mídia.
        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(empresaId, conversaId, wamid, "media-1"));

        mensagem.MidiaChave.Should().Be($"atendimento/{empresaId}/{conversaId}/{wamid}.jpg");
        mensagem.MidiaMime.Should().Be("image/jpeg");
        await unitOfWork.Received(1).CommitAsync();
    }
}
