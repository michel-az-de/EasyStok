using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #1444: a dona grava áudio no console e ele sai pelo WhatsApp. O WebM/Opus do Chrome vira Ogg/Opus no
/// backend (a Meta não aceita WebM), sobe no storage público e a mensagem da dona fica com o áudio tocável.
/// </summary>
public partial class AtendimentoConversasControllerTests
{
    private const string UrlDoAudio = "https://app.easystok.online/files/atendimento/x/voz.ogg";

    private ICanalComAudio CanalComAudio => (ICanalComAudio)_canal;

    private void StorageDevolveAudio() =>
        _fileStorage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult("atendimento/x/voz.ogg", UrlDoAudio, "audio/ogg", 100));

    [Fact]
    public async Task AudioDoChromeViraOggESaiComoNotaDeVoz()
    {
        var conversa = ConversaComClienteAgora();
        StorageDevolveAudio();
        CanalComAudio.EnviarAudioAsync(WaId, UrlDoAudio, true, Arg.Any<CancellationToken>()).Returns("wamid.voz1");

        var result = await _controller.EnviarAudio(conversa.Id, Arquivo(WebmOpusMono(), "audio.webm", "audio/webm;codecs=opus"), default);

        var dados = Dados<MensagemAtendimentoResult>(result);
        dados.TipoConteudo.Should().Be(TipoConteudoMensagem.Audio);
        dados.MidiaMime.Should().Be("audio/ogg");
        await _fileStorage.Received(1).UploadAsync(
            Arg.Is<FileUploadRequest>(r => r.ContentType == "audio/ogg" && r.FileName.EndsWith(".ogg")
                && r.BucketPath == $"atendimento/{_empresaId}/{conversa.Id}" && r.IsPublic
                && Encoding.ASCII.GetString(r.Content, 0, 4) == "OggS"),
            Arg.Any<CancellationToken>());
        await CanalComAudio.Received(1).EnviarAudioAsync(WaId, UrlDoAudio, true, Arg.Any<CancellationToken>());
        _repositorio.Mensagens.Should().ContainSingle(m => m.Direcao == DirecaoMensagem.Saida
            && m.Autor == AutorMensagem.Dona && m.TipoConteudo == TipoConteudoMensagem.Audio
            && m.MidiaChave == "atendimento/x/voz.ogg" && m.MidiaMime == "audio/ogg" && m.ExternoId == "wamid.voz1");
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
    }

    [Fact]
    public async Task AudioEmFormatoQueAMetaNaoAceita400SemEnviar()
    {
        var conversa = ConversaComClienteAgora();

        var result = await _controller.EnviarAudio(conversa.Id, Arquivo(Encoding.ASCII.GetBytes("RIFF....WAVEfmt "), "a.wav", "audio/wav"), default);

        result.Should().BeOfType<BadRequestObjectResult>();
        await CanalComAudio.DidNotReceiveWithAnyArgs().EnviarAudioAsync(default!, default!, default, default);
        await _fileStorage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
    }

    [Fact]
    public async Task AudioVazio400()
    {
        var conversa = ConversaComClienteAgora();

        var result = await _controller.EnviarAudio(conversa.Id, Arquivo([], "audio.webm", "audio/webm"), default);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task FalhaDaMetaGravaOAudioComoFalhouE502()
    {
        var conversa = ConversaComClienteAgora();
        StorageDevolveAudio();
        CanalComAudio.EnviarAudioAsync(WaId, UrlDoAudio, true, Arg.Any<CancellationToken>())
            .ThrowsAsync(new WhatsAppCloudException(131053, "Media upload error", ehPermanente: true, statusHttp: 400));

        var result = await _controller.EnviarAudio(conversa.Id, Arquivo(WebmOpusMono(), "audio.webm", "audio/webm"), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
        _repositorio.Mensagens.Should().ContainSingle(m => m.TipoConteudo == TipoConteudoMensagem.Audio
            && m.Status == StatusMensagem.Falhou && m.MidiaChave == "atendimento/x/voz.ogg");
    }

    private static FormFile Arquivo(byte[] conteudo, string nome, string tipo) =>
        new(new MemoryStream(conteudo), 0, conteudo.Length, "file", nome)
        {
            Headers = new HeaderDictionary(),
            ContentType = tipo,
        };

    /// <summary>
    /// WebM mínimo como o do Chrome com <c>start(fatia)</c>: Segment e Cluster de tamanho desconhecido,
    /// faixa A_OPUS mono com OpusHead e dois pacotes CELT de 20 ms.
    /// </summary>
    private static byte[] WebmOpusMono()
    {
        byte[] Elemento(byte[] id, byte[] corpo) => [.. id, (byte)(0x80 | corpo.Length), .. corpo];
        byte[] opusHead = [.. "OpusHead"u8, 1, 1, 0, 0, 0x80, 0xBB, 0, 0, 0, 0, 0];
        byte[] cabecalhoEbml = Elemento([0x1A, 0x45, 0xDF, 0xA3], Elemento([0x42, 0x82], [.. "webm"u8]));
        byte[] faixa = Elemento([0xAE], [
            .. Elemento([0xD7], [1]),
            .. Elemento([0x86], [.. "A_OPUS"u8]),
            .. Elemento([0x63, 0xA2], opusHead),
        ]);
        byte[] faixas = Elemento([0x16, 0x54, 0xAE, 0x6B], faixa);
        byte[] desconhecido = [0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        byte[] pacote = [0xFC, 0x01, 0x02, 0x03];
        byte[] bloco = Elemento([0xA3], [0x81, 0x00, 0x00, 0x80, .. pacote]);
        return [
            .. cabecalhoEbml,
            0x18, 0x53, 0x80, 0x67, .. desconhecido,
            .. faixas,
            0x1F, 0x43, 0xB6, 0x75, .. desconhecido,
            .. Elemento([0xE7], [0]),
            .. bloco, .. bloco,
        ];
    }
}
