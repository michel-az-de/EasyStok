using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>
/// #2027: a foto que o console manda na conversa sai da FotoUrl do prato. Apontada para o arquivo de outra
/// empresa, o arquivo dela era baixado e enviado ao cliente desta.
/// </summary>
public class EnviarImagemCardapioDaOutraEmpresaTests
{
    [Fact]
    public async Task FotoUrlDeOutraVitrine_Da404SemLerOArquivo()
    {
        var empresa = Guid.NewGuid();
        var vitrine = StorefrontEntity.Criar(empresa, "casa", "Casa", 0);
        var item = CardapioItem.CriarAvulso(vitrine.Id, "Lasanha", 35m);
        var alheia = $"cardapios/{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid()}/foto.webp";
        item.AtualizarMetadata(fotoUrl: $"/files/{alheia}");
        var vitrines = Substitute.For<IStorefrontRepository>();
        vitrines.GetByEmpresaAsync(empresa, Arg.Any<CancellationToken>()).Returns(vitrine);
        var itens = Substitute.For<ICardapioItemRepository>();
        itens.GetByIdAndScopeAsync(vitrine.Id, item.Id, empresa, Arg.Any<CancellationToken>()).Returns(item);
        var storage = Substitute.For<IFileStorage>();
        storage.ExistsAsync(alheia, Arg.Any<CancellationToken>()).Returns(true);
        storage.DownloadAsync(alheia, Arg.Any<CancellationToken>()).Returns([1, 2, 3]);
        var sut = new EnviarImagemCardapioConsoleUseCase(vitrines, itens, storage, enviarMensagem: null!);

        var enviar = () => sut.ExecuteAsync(new EnviarImagemCardapioConsoleCommand(
            empresa, Guid.NewGuid(), Guid.NewGuid(), item.Id, 0, null));

        await enviar.Should().ThrowAsync<FotoCardapioNaoEncontradaException>();
        await storage.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
    }
}
