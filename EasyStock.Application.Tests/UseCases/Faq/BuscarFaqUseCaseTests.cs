using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faq;

namespace EasyStock.Application.Tests.UseCases.Faq;

public class BuscarFaqUseCaseTests
{
    private static FaqItem NovoItem(string titulo, FaqCategoria categoria, int visualizacoes = 0)
    {
        var item = FaqItem.Criar(categoria.Id, titulo, titulo.ToLowerInvariant().Replace(" ", "-"), $"# {titulo}\n\nconteudo");
        for (int i = 0; i < visualizacoes; i++) item.RegistrarVisualizacao();
        item.Categoria = categoria;
        return item;
    }

    [Fact]
    public async Task ExecuteAsync_aplica_clamp_de_pagesize()
    {
        var repo = Substitute.For<IFaqRepository>();
        repo.BuscarAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<FaqItem>, int)>((new List<FaqItem>(), 0)));

        var uc = new BuscarFaqUseCase(repo);

        await uc.ExecuteAsync(new BuscarFaqQuery(null, null, Page: 0, PageSize: 9999));

        await repo.Received(1).BuscarAsync(null, null, 1, 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_mapeia_itens_e_total()
    {
        var cat = FaqCategoria.Criar("Cadastros", "cadastros");
        var itens = new List<FaqItem>
        {
            NovoItem("Como cadastrar produto", cat, 100),
            NovoItem("Como criar cliente", cat, 50)
        };
        var repo = Substitute.For<IFaqRepository>();
        repo.BuscarAsync(Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<FaqItem>, int)>((itens, 2)));

        var uc = new BuscarFaqUseCase(repo);
        var result = await uc.ExecuteAsync(new BuscarFaqQuery("produto"));

        result.Total.Should().Be(2);
        result.Itens.Should().HaveCount(2);
        result.Itens[0].Titulo.Should().Be("Como cadastrar produto");
        result.Itens[0].CategoriaNome.Should().Be("Cadastros");
        result.Itens[0].CategoriaSlug.Should().Be("cadastros");
    }
}
