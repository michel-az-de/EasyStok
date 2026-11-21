using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Application.Ports.Output.Storage;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #2440 (A1): mídia e foto do cardápio em conversa de OUTRA atendente passam pela mesma guarda do texto
/// (ATD-1, #2305): 409 CONVERSA_DE_OUTRA sem confirmar e nada sai; com <c>confirmar</c> envia; livre ou própria envia.
/// </summary>
public partial class AtendimentoConversasControllerTests
{
    private Conversa ConversaDeOutra()
    {
        var conversa = ConversaComClienteAgora();
        conversa.Assumir(DateTime.UtcNow, Guid.NewGuid());
        return conversa;
    }

    private void Conflito409DeOutra(IActionResult result) =>
        result.Should().BeOfType<ConflictObjectResult>().Which.Value.Should().BeOfType<ApiErrorResponse>()
            .Which.Error.Code.Should().Be(ConversaAtribuidaOutraException.Codigo);

    [Fact]
    public async Task AudioEmConversaDeOutraSemConfirmarRecusa409ENadaSai()
    {
        var conversa = ConversaDeOutra();
        StorageDevolveAudio();

        Conflito409DeOutra(await _controller.EnviarAudio(conversa.Id, Arquivo(WebmOpusMono(), "audio.webm", "audio/webm"), confirmar: false));

        await CanalComAudio.DidNotReceiveWithAnyArgs().EnviarAudioAsync(default!, default!, default, default);
        await _fileStorage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
        _repositorio.Mensagens.Should().NotContain(m => m.Direcao == EasyStock.Domain.Enums.Atendimento.DirecaoMensagem.Saida);
    }

    [Fact]
    public async Task AudioEmConversaDeOutraComConfirmarEnvia()
    {
        var conversa = ConversaDeOutra();
        StorageDevolveAudio();
        CanalComAudio.EnviarAudioAsync(WaId, UrlDoAudio, true, Arg.Any<CancellationToken>()).Returns("wamid.voz2");

        var result = await _controller.EnviarAudio(conversa.Id, Arquivo(WebmOpusMono(), "audio.webm", "audio/webm"), confirmar: true);

        Dados<MensagemAtendimentoResult>(result).Should().NotBeNull();
        await CanalComAudio.Received(1).EnviarAudioAsync(WaId, UrlDoAudio, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AudioEmConversaLivreOuPropriaNaoPedeConfirmacao()
    {
        var livre = ConversaComClienteAgora();
        var propria = ConversaComClienteAgora();
        propria.Assumir(DateTime.UtcNow, _usuarioId);
        StorageDevolveAudio();
        CanalComAudio.EnviarAudioAsync(WaId, UrlDoAudio, true, Arg.Any<CancellationToken>()).Returns("wamid.voz3");

        Dados<MensagemAtendimentoResult>(await _controller.EnviarAudio(livre.Id, Arquivo(WebmOpusMono(), "a.webm", "audio/webm"))).Should().NotBeNull();
        Dados<MensagemAtendimentoResult>(await _controller.EnviarAudio(propria.Id, Arquivo(WebmOpusMono(), "a.webm", "audio/webm"))).Should().NotBeNull();
    }

    [Fact]
    public async Task ImagemEmConversaDeOutraSemConfirmarRecusa409ENadaSai()
    {
        var conversa = ConversaDeOutra();

        Conflito409DeOutra(await _controller.EnviarImagem(conversa.Id, Arquivo([0xFF, 0xD8, 0xFF, 0xE0, 1], "f.jpg", "image/jpeg"), null, confirmar: false));

        await _canal.DidNotReceiveWithAnyArgs().EnviarImagemAsync(default!, default!, default, default);
        await _fileStorage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
    }

    private void PrepararFotoDoCardapio(out EasyStock.Domain.Entities.Storefront.CardapioItem item)
    {
        item = ItemComGaleria(ChaveDaVitrine("capa.webp"));
        _fileStorage.ExistsAsync(ChaveDaVitrine("capa.webp"), Arg.Any<CancellationToken>()).Returns(true);
        _fileStorage.DownloadAsync(ChaveDaVitrine("capa.webp"), Arg.Any<CancellationToken>()).Returns(WebpDaGaleria);
        _imageProcessor.Optimize(WebpDaGaleria, "image/webp", 1920, 85, true).Returns((JpegConvertido, "image/jpeg", ".jpg"));
        _fileStorage.UploadAsync(Arg.Any<FileUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFileResult("atendimento/x/foto.jpg", "https://app.easystok.online/files/atendimento/x/foto.jpg", "image/jpeg", 7));
        _canal.EnviarImagemAsync(WaId, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns("wamid.foto9");
    }

    [Fact]
    public async Task ImagemCardapioEmConversaDeOutraSemConfirmarRecusa409ENadaSai()
    {
        var conversa = ConversaDeOutra();
        PrepararFotoDoCardapio(out var item);

        Conflito409DeOutra(await _controller.EnviarImagemCardapio(conversa.Id, new EnviarImagemCardapioBody(item.Id, 0, null), ImagemCardapio(), default));

        await _canal.DidNotReceiveWithAnyArgs().EnviarImagemAsync(default!, default!, default, default);
        await _fileStorage.DidNotReceiveWithAnyArgs().UploadAsync(default!, default);
    }

    [Fact]
    public async Task ImagemCardapioEmConversaDeOutraComConfirmarEnvia()
    {
        var conversa = ConversaDeOutra();
        PrepararFotoDoCardapio(out var item);

        var result = await _controller.EnviarImagemCardapio(
            conversa.Id, new EnviarImagemCardapioBody(item.Id, 0, null, Confirmar: true), ImagemCardapio(), default);

        Dados<MensagemAtendimentoResult>(result).Should().NotBeNull();
        await _canal.Received(1).EnviarImagemAsync(WaId, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
