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
            conversaRepository, armazenador, Substitute.For<ITenantContextAccessor>(), unitOfWork, TimeProvider.System,
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
            tenantContext, Substitute.For<IUnitOfWork>(), TimeProvider.System, NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance);

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
            Substitute.For<ITenantContextAccessor>(), unitOfWork, TimeProvider.System, NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance,
            new TranscricaoAudioWhatsApp(transcritor, fileStorage, unitOfWork, NullLogger<TranscricaoAudioWhatsApp>.Instance));

        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(empresaId, conversaId, "wamid.a1", "media-a"));

        mensagem.MidiaChave.Should().NotBeNull();
        mensagem.Transcricao.Should().Be("oi, tudo bem?");
    }

    [Fact]
    public async Task AudioTranscrito_DisparaTurnoDoAgenteDepoisDaTranscricao()
    {
        // #1406: com transcritor, o webhook nao enfileira o turno do audio; o job de midia o faz depois de transcrever.
        var (processador, mensagem, fila, transcritor) = CriarAudio(falhaDownload: false);
        var ordem = new List<string>();
        transcritor.When(t => t.TranscreverAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => ordem.Add("transcricao"));
        fila.When(f => f.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>()))
            .Do(_ => ordem.Add("turno"));

        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(mensagem.EmpresaId, mensagem.ConversaId, "wamid.t", "media-t"));

        await fila.Received(1).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente,
            Arg.Is<ProcessarTurnoAgenteJob>(j => j.EmpresaId == mensagem.EmpresaId && j.ConversaId == mensagem.ConversaId));
        ordem.Should().Equal("transcricao", "turno");
    }

    [Fact]
    public async Task AudioComFalhaNoDownload_DisparaTurnoSoNaPrimeiraFalha()
    {
        // #1406: sem transcricao possivel o agente ainda responde (pede para escrever), mas uma vez so.
        var (processador, mensagem, fila, _) = CriarAudio(falhaDownload: true);
        var job = new ArmazenarMidiaWhatsAppJob(mensagem.EmpresaId, mensagem.ConversaId, "wamid.t", "media-t");

        await processador.ExecuteAsync(job);
        await processador.ExecuteAsync(job);

        await fila.Received(1).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
    }

    [Fact]
    public async Task Imagem_NaoDisparaTurnoPeloJobDeMidia()
    {
        var mensagem = Domain.Entities.Atendimento.Mensagem.Entrada(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Imagem, externoId: "wamid.t");
        var (processador, _, fila, _) = CriarAudio(falhaDownload: false, mensagem);

        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(mensagem.EmpresaId, mensagem.ConversaId, "wamid.t", "media-t"));

        await fila.DidNotReceive().EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, Arg.Any<ProcessarTurnoAgenteJob>());
    }

    private static (ProcessarMidiaWhatsAppJobUseCase, Domain.Entities.Atendimento.Mensagem, IQueueService, EasyStock.Application.Ports.Output.Ai.ITranscritorAudio)
        CriarAudio(bool falhaDownload, Domain.Entities.Atendimento.Mensagem? mensagem = null)
    {
        mensagem ??= Domain.Entities.Atendimento.Mensagem.Entrada(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Audio, externoId: "wamid.t");
        var conversaRepository = Substitute.For<IConversaRepository>();
        conversaRepository.ObterMensagemPorExternoIdAsync(mensagem.EmpresaId, "wamid.t", Arg.Any<CancellationToken>()).Returns(mensagem);
        var cloudClient = Substitute.For<IWhatsAppCloudClient>();
        if (falhaDownload)
            cloudClient.BaixarMidiaAsync("media-t", Arg.Any<CancellationToken>())
                .Returns<(Stream, string)>(_ => throw new HttpRequestException("Meta fora"));
        else
            cloudClient.BaixarMidiaAsync("media-t", Arg.Any<CancellationToken>())
                .Returns(_ => ((Stream)new MemoryStream([1, 2]), "audio/ogg"));
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
        transcritor.TranscreverAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("quero um bolo");
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var fila = Substitute.For<IQueueService>();
        var processador = new ProcessarMidiaWhatsAppJobUseCase(
            conversaRepository, new ArmazenadorMidiaWhatsApp(cloudClient, fileStorage),
            Substitute.For<ITenantContextAccessor>(), unitOfWork, TimeProvider.System, NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance,
            new TranscricaoAudioWhatsApp(transcritor, fileStorage, unitOfWork, NullLogger<TranscricaoAudioWhatsApp>.Instance),
            fila);
        return (processador, mensagem, fila, transcritor);
    }

    [Fact]
    public async Task FalhaNoDownload_GravaErroNaMensagemEReagenda()
    {
        // #1397: antes o catch só logava e o balão ficava em "Foto" para sempre.
        var empresaId = Guid.NewGuid();
        var mensagem = Domain.Entities.Atendimento.Mensagem.Entrada(
            empresaId, Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Imagem, externoId: "wamid.f");
        mensagem.AguardarMidia("media-f", DateTime.UtcNow);
        var conversaRepository = Substitute.For<IConversaRepository>();
        conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, "wamid.f", Arg.Any<CancellationToken>()).Returns(mensagem);
        var cloudClient = Substitute.For<IWhatsAppCloudClient>();
        cloudClient.BaixarMidiaAsync("media-f", Arg.Any<CancellationToken>())
            .Returns<(Stream, string)>(_ => throw new HttpRequestException("Meta fora"));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var processador = new ProcessarMidiaWhatsAppJobUseCase(
            conversaRepository, new ArmazenadorMidiaWhatsApp(cloudClient, Substitute.For<IFileStorage>()),
            Substitute.For<ITenantContextAccessor>(), unitOfWork, TimeProvider.System,
            NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance);

        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(empresaId, mensagem.ConversaId, "wamid.f", "media-f"));

        mensagem.TentativasMidia.Should().Be(1);
        mensagem.ErroMidia.Should().Contain("Meta fora");
        mensagem.ProximaTentativaMidiaEm.Should().NotBeNull("a varredura tenta de novo");
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task MidiaJaAnexada_NaoBaixaDeNovo()
    {
        var empresaId = Guid.NewGuid();
        var mensagem = Domain.Entities.Atendimento.Mensagem.Entrada(
            empresaId, Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Imagem, externoId: "wamid.j");
        mensagem.AnexarMidia("atendimento/j.jpg", "image/jpeg");
        var conversaRepository = Substitute.For<IConversaRepository>();
        conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, "wamid.j", Arg.Any<CancellationToken>()).Returns(mensagem);
        var cloudClient = Substitute.For<IWhatsAppCloudClient>();
        var processador = new ProcessarMidiaWhatsAppJobUseCase(
            conversaRepository, new ArmazenadorMidiaWhatsApp(cloudClient, Substitute.For<IFileStorage>()),
            Substitute.For<ITenantContextAccessor>(), Substitute.For<IUnitOfWork>(), TimeProvider.System,
            NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance);

        await processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(empresaId, mensagem.ConversaId, "wamid.j", "media-j"));

        await cloudClient.DidNotReceiveWithAnyArgs().BaixarMidiaAsync(default!, default);
    }

    [Fact]
    public async Task Varredura_ReservaPendentesESobreviveARestart()
    {
        // #1397: a fila em memória some no restart; a pendência fica no banco e a varredura da partida a retoma.
        var empresaId = Guid.NewGuid();
        var mensagem = Domain.Entities.Atendimento.Mensagem.Entrada(
            empresaId, Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Audio, externoId: "wamid.r");
        mensagem.AguardarMidia("media-r", DateTime.UtcNow.AddHours(-1));
        var repository = Substitute.For<IConversaRepository>();
        repository.ListarMidiasPendentesComLockAsync(Arg.Any<DateTime>(), 20, Arg.Any<CancellationToken>())
            .Returns([mensagem]);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.ExecuteInTransactionSemRetryAsync(
                Arg.Any<Func<CancellationToken, Task<IReadOnlyList<ArmazenarMidiaWhatsAppJob>>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<IReadOnlyList<ArmazenarMidiaWhatsAppJob>>>>()(CancellationToken.None));

        var jobs = await new ReservarMidiasPendentesUseCase(repository, unitOfWork, TimeProvider.System).ExecuteAsync(20);

        jobs.Should().ContainSingle().Which.Should().Be(
            new ArmazenarMidiaWhatsAppJob(empresaId, mensagem.ConversaId, "wamid.r", "media-r"));
        mensagem.ProximaTentativaMidiaEm.Should().BeAfter(DateTime.UtcNow, "reservada: outro processo não pega a mesma");
        await unitOfWork.Received(1).CommitAsync();
    }
    [Fact]
    public async Task CancelamentoNaTranscricao_NaoMarcaErroNoAnexoSalvo()
    {
        // #1411: a mídia já foi gravada; cancelar a transcrição não pode virar erro de mídia nem reagendar o download.
        var empresaId = Guid.NewGuid();
        var mensagem = Domain.Entities.Atendimento.Mensagem.Entrada(
            empresaId, Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Audio, externoId: "wamid.c");
        var conversaRepository = Substitute.For<IConversaRepository>();
        conversaRepository.ObterMensagemPorExternoIdAsync(empresaId, "wamid.c", Arg.Any<CancellationToken>()).Returns(mensagem);
        var cloudClient = Substitute.For<IWhatsAppCloudClient>();
        cloudClient.BaixarMidiaAsync("media-c", Arg.Any<CancellationToken>())
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
        using var cts = new CancellationTokenSource();
        var transcritor = Substitute.For<EasyStock.Application.Ports.Output.Ai.ITranscritorAudio>();
        transcritor.Disponivel.Returns(true);
        transcritor.TranscreverAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var processador = new ProcessarMidiaWhatsAppJobUseCase(
            conversaRepository, new ArmazenadorMidiaWhatsApp(cloudClient, fileStorage),
            Substitute.For<ITenantContextAccessor>(), unitOfWork, TimeProvider.System, NullLogger<ProcessarMidiaWhatsAppJobUseCase>.Instance,
            new TranscricaoAudioWhatsApp(transcritor, fileStorage, unitOfWork, NullLogger<TranscricaoAudioWhatsApp>.Instance));

        await Record.ExceptionAsync(() =>
            processador.ExecuteAsync(new ArmazenarMidiaWhatsAppJob(empresaId, mensagem.ConversaId, "wamid.c", "media-c"), cts.Token));

        mensagem.MidiaChave.Should().NotBeNull();
        mensagem.ErroMidia.Should().BeNull();
        mensagem.TentativasMidia.Should().Be(0);
    }
}
