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
}
