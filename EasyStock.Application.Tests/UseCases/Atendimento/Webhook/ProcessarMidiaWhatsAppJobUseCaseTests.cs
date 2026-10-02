using EasyStock.Application.Ports.Output;
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
            conversaRepository, armazenador, Substitute.For<ITenantContextAccessor>(), unitOfWork,
            NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance);

        // O enfileiramento em si já é coberto por ProcessarEventoWhatsAppUseCaseTests.ImagemArmazena;
        // aqui prova-se que DRENAR o job (o que o AtendimentoFilaMidiaBackgroundService faz em
        // produção) efetivamente baixa e anexa a mídia.
        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(empresaId, conversaId, wamid, "media-1"));

        mensagem.MidiaChave.Should().Be($"atendimento/{empresaId}/{conversaId}/{wamid}.jpg");
        mensagem.MidiaMime.Should().Be("image/jpeg");
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DefineTenantAntesDeBuscarMensagem()
    {
        // O job roda num escopo sem JWT: sem o tenant, o filtro global e a RLS zeram a consulta.
        var empresaId = Guid.NewGuid();
        var conversaRepository = Substitute.For<IConversaRepository>();
        var tenantContext = Substitute.For<ITenantContextAccessor>();
        var processador = new ProcessarMidiaWhatsAppJobUseCase(
            conversaRepository,
            new ArmazenadorMidiaWhatsApp(Substitute.For<IWhatsAppCloudClient>(), Substitute.For<IFileStorage>()),
            tenantContext, Substitute.For<IUnitOfWork>(), NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance);

        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(empresaId, Guid.NewGuid(), "wamid.x", "media-x"));

        Received.InOrder(() =>
        {
            tenantContext.SetCurrentTenant(empresaId);
            conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, "wamid.x", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AudioArmazenadoETranscrito()
    {
        // #1398: depois do commit da mídia, o áudio é transcrito e o texto vai para a mensagem.
        var empresaId = Guid.NewGuid();
        var conversaId = Guid.NewGuid();
        var mensagem = Domain.Entities.Atendimento.Mensagem.Entrada(
            empresaId, conversaId, DateTime.UtcNow, TipoConteudoMensagem.Audio, externoId: "wamid.a1");
        var conversaRepository = Substitute.For<IConversaRepository>();
        conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, "wamid.a1", Arg.Any<CancellationToken>())
            .Returns(mensagem);
        var cloudClient = Substitute.For<IWhatsAppCloudClient>();
        cloudClient.BaixarMidiaAsync("media-a", Arg.Any<CancellationToken>())
            .Returns(((Stream)new MemoryStream([1, 2]), "audio/ogg"));
        var fileStorage = Substitute.For<IFileStorage>();
        fileStorage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = ci.Arg<FileUploadRequest>();
                return Task.FromResult(new StoredFileResult(
                    $"{req.BucketPath}/{req.FileName}", "https://storage.test/x", req.ContentType, req.Content.Length));
            });
        fileStorage.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2 });
        var transcritor = Substitute.For<EasyStock.Application.Ports.Output.Ai.ITranscritorAudio>();
        transcritor.Disponivel.Returns(true);
        transcritor.TranscreverAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("oi, tudo bem?");
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var processador = new ProcessarMidiaWhatsAppJobUseCase(
            conversaRepository, new ArmazenadorMidiaWhatsApp(cloudClient, fileStorage),
            Substitute.For<ITenantContextAccessor>(), unitOfWork, NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance,
            new TranscricaoAudioWhatsApp(transcritor, fileStorage, unitOfWork, NullLogger<TranscricaoAudioWhatsApp>.Instance));

        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(empresaId, conversaId, "wamid.a1", "media-a"));

        mensagem.MidiaChave.Should().NotBeNull();
        mensagem.Transcricao.Should().Be("oi, tudo bem?");
    }
}
