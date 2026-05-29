using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faq.Admin;

namespace EasyStock.Application.Tests.UseCases.Faq;

public class CriarFaqItemUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_falha_se_categoria_nao_existe()
    {
        var repo = Substitute.For<IFaqAdminRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        repo.ObterCategoriaAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FaqCategoria?>(null));

        var uc = new CriarFaqItemUseCase(repo, uow);

        var act = () => uc.ExecuteAsync(new CriarFaqItemCommand(
            Guid.NewGuid(), "Titulo", "slug", "conteudo", null, null, 0, null));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*nao encontrada*");
    }

    [Fact]
    public async Task ExecuteAsync_falha_se_slug_ja_existe_na_categoria()
    {
        var cat = FaqCategoria.Criar("Cadastros", "cadastros");
        var repo = Substitute.For<IFaqAdminRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        repo.ObterCategoriaAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FaqCategoria?>(cat));
        repo.ItemSlugExisteAsync(Arg.Any<Guid>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var uc = new CriarFaqItemUseCase(repo, uow);

        var act = () => uc.ExecuteAsync(new CriarFaqItemCommand(
            cat.Id, "Titulo", "como-fazer", "conteudo", null, null, 0, null));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Ja existe item*");
    }

    [Fact]
    public async Task ExecuteAsync_cria_item_em_rascunho_quando_dados_validos()
    {
        var cat = FaqCategoria.Criar("Cadastros", "cadastros");
        var repo = Substitute.For<IFaqAdminRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        repo.ObterCategoriaAsync(cat.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FaqCategoria?>(cat));
        repo.ItemSlugExisteAsync(Arg.Any<Guid>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var uc = new CriarFaqItemUseCase(repo, uow);

        var result = await uc.ExecuteAsync(new CriarFaqItemCommand(
            cat.Id, "Como cadastrar produto", "como-cadastrar-produto", "# titulo\nconteudo",
            null, new[] { "produto" }, 1, Guid.NewGuid()));

        result.ItemId.Should().NotBeEmpty();
        result.Slug.Should().Be("como-cadastrar-produto");
        await repo.Received(1).InserirItemAsync(Arg.Is<FaqItem>(i =>
            i.Titulo == "Como cadastrar produto"
            && i.CategoriaId == cat.Id
            && i.Status == Domain.Enums.FaqStatus.Rascunho), Arg.Any<CancellationToken>());
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ExecuteAsync_normaliza_slug_para_minusculo()
    {
        var cat = FaqCategoria.Criar("Cat", "cat");
        var repo = Substitute.For<IFaqAdminRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        repo.ObterCategoriaAsync(cat.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FaqCategoria?>(cat));
        repo.ItemSlugExisteAsync(Arg.Any<Guid>(), "como-fazer", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var uc = new CriarFaqItemUseCase(repo, uow);

        var result = await uc.ExecuteAsync(new CriarFaqItemCommand(
            cat.Id, "T", "  COMO-FAZER  ", "conteudo", null, null, 0, null));

        result.Slug.Should().Be("como-fazer");
    }
}
