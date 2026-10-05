using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class TranscricaoAudioWhatsAppTests
{
    private readonly ITranscritorAudio _transcritor = Substitute.For<ITranscritorAudio>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public TranscricaoAudioWhatsAppTests()
    {
        _transcritor.Disponivel.Returns(true);
        _storage.DownloadAsync("atendimento/a.ogg", Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3 });
    }

    private TranscricaoAudioWhatsApp Criar() =>
        new(_transcritor, _storage, _unitOfWork, NullLogger<TranscricaoAudioWhatsApp>.Instance);

    private static Mensagem Audio()
    {
        var m = Mensagem.Entrada(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Audio,
            externoId: "wamid.audio");
        m.AnexarMidia("atendimento/a.ogg", "audio/ogg");
        return m;
    }

    [Fact]
    public async Task GravaTranscricaoNaMensagem()
    {
        _transcritor.TranscreverAsync(Arg.Any<byte[]>(), "audio/ogg", Arg.Any<CancellationToken>())
            .Returns(" quero um bolo ");
        var mensagem = Audio();

        await Criar().TranscreverAsync(mensagem);

        mensagem.Transcricao.Should().Be("quero um bolo");
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task FalhaDoTranscritorSoLoga()
    {
        _transcritor.TranscreverAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new HttpRequestException("503"));
        var mensagem = Audio();

        var acao = () => Criar().TranscreverAsync(mensagem);

        await acao.Should().NotThrowAsync();
        mensagem.Transcricao.Should().BeNull();
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DesligadoNaoBaixaNemTranscreve()
    {
        _transcritor.Disponivel.Returns(false);
        var mensagem = Audio();

        await Criar().TranscreverAsync(mensagem);

        mensagem.Transcricao.Should().BeNull();
        await _storage.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
        await _transcritor.DidNotReceiveWithAnyArgs().TranscreverAsync(default!, default!, default);
    }

    [Fact]
    public async Task ImagemNaoETranscrita()
    {
        var mensagem = Mensagem.Entrada(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, TipoConteudoMensagem.Imagem,
            externoId: "wamid.img");
        mensagem.AnexarMidia("atendimento/a.jpg", "image/jpeg");

        await Criar().TranscreverAsync(mensagem);

        await _transcritor.DidNotReceiveWithAnyArgs().TranscreverAsync(default!, default!, default);
    }
}
