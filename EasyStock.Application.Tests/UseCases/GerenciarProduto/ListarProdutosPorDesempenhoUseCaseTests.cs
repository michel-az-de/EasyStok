using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarProduto;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.GerenciarProduto;

/// <summary>#3303: a lista de Produtos ordena pelas vendas e filtra pelos sinais com a mesma regra da faixa.</summary>
public sealed class ListarProdutosPorDesempenhoUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 11, 25, 2, 30, 0, DateTimeKind.Utc);
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Lasanha = Guid.NewGuid(), Linguini = Guid.NewGuid(), Molho = Guid.NewGuid(),
        Novo = Guid.NewGuid(), Farinha = Guid.NewGuid(), Agnolotti = Guid.NewGuid();
    private static readonly Guid Massas = Guid.NewGuid(), Insumos = Guid.NewGuid();

    private static DateTime DiasAtras(int dias) => Agora.AddDays(-dias);

    private static (ListarProdutosPorDesempenhoUseCase UseCase, DesempenhoDosProdutosUseCase Desempenho, IProdutoRepository Repo) Montar()
    {
        var consulta = Substitute.For<IProdutosDesempenhoQuery>();
        consulta.CatalogoAsync(Empresa, Arg.Any<CancellationToken>()).Returns(new List<ProdutoDoCatalogo>
        {
            new(Lasanha, "Lasanha", true, DiasAtras(300), true, 45m, 15m),
            new(Linguini, "Linguini", true, DiasAtras(300), true, 19m, null),
            new(Molho, "Molho", false, DiasAtras(300), true, 18m, null),
            new(Novo, "Novo", true, DiasAtras(40), true, null, null),
            new(Farinha, "Farinha", true, DiasAtras(300), false, null, null),
            new(Agnolotti, "Agnolotti", true, DiasAtras(300), true, 30m, null),
        });
        var vendas = new List<VendaDoProdutoNoPedido>();
        for (var i = 0; i < 4; i++) vendas.Add(new(Lasanha, Guid.NewGuid(), DiasAtras(10 + i * 15), 80m, 2m));
        for (var i = 0; i < 3; i++) vendas.Add(new(Linguini, Guid.NewGuid(), DiasAtras(110 + i * 5), 19m, 1m));
        vendas.Add(new(Molho, Guid.NewGuid(), DiasAtras(5), 15m, 1m));
        consulta.VendasDesdeAsync(Empresa, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(vendas);
        consulta.HistoricoAsync(Empresa, Arg.Any<CancellationToken>()).Returns(new List<HistoricoDoProduto>
        {
            new(Lasanha, 4, DiasAtras(55), DiasAtras(10)),
            new(Linguini, 3, DiasAtras(120), DiasAtras(110)),
            new(Molho, 1, DiasAtras(5), DiasAtras(5)),
        });
        var desempenho = new DesempenhoDosProdutosUseCase(consulta, new FakeTimeProvider(new DateTimeOffset(Agora)));

        Produto Entidade(Guid id, string nome, StatusProduto status, Guid categoria) =>
            new() { Id = id, EmpresaId = Empresa, Nome = nome, Status = status, CategoriaId = categoria };
        var entidades = new List<Produto>
        {
            Entidade(Lasanha, "Lasanha", StatusProduto.Ativo, Massas),
            Entidade(Linguini, "Linguini", StatusProduto.Ativo, Massas),
            Entidade(Molho, "Molho", StatusProduto.Inativo, Massas),
            Entidade(Novo, "Novo", StatusProduto.Ativo, Massas),
            Entidade(Farinha, "Farinha", StatusProduto.Ativo, Insumos),
            Entidade(Agnolotti, "Agnolotti", StatusProduto.Ativo, Massas),
        };
        var repo = Substitute.For<IProdutoRepository>();
        // O banco devolve fora de ordem: quem ordena é o caso de uso.
        repo.GetByIdsAsync(Empresa, Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(c => (IReadOnlyList<Produto>)entidades.Where(e => c.Arg<IEnumerable<Guid>>().Contains(e.Id)).Reverse().ToList());
        return (new ListarProdutosPorDesempenhoUseCase(desempenho, repo), desempenho, repo);
    }

    private static Task<(IReadOnlyList<Produto> Produtos, int Total)> Listar(
        ListarProdutosPorDesempenhoUseCase useCase, string? sort, SinalDoProduto? sinal = null,
        StatusProduto? status = null, Guid? categoriaId = null, int page = 1, int pageSize = 20, string? order = null)
        => useCase.ExecuteAsync(Empresa, new FiltroPorDesempenho(sort, order, sinal, status, categoriaId), page, pageSize);

    [Fact]
    public async Task Maior_receita_primeiro_e_empate_por_nome()
    {
        var (useCase, _, _) = Montar();
        var (produtos, total) = await Listar(useCase, "receita90");

        produtos.Select(p => p.Nome).Should().Equal("Lasanha", "Molho", "Agnolotti", "Farinha", "Linguini", "Novo");
        total.Should().Be(6);
    }

    [Fact]
    public async Task Sem_vender_ha_mais_tempo_poe_ativos_de_venda_antes_dos_que_nunca_venderam_e_do_resto()
    {
        var (useCase, _, _) = Montar();
        var (produtos, _) = await Listar(useCase, "semvender");

        produtos.Select(p => p.Nome).Should().Equal("Linguini", "Lasanha", "Agnolotti", "Novo", "Farinha", "Molho");
    }

    [Fact]
    public async Task Pagina_corta_depois_de_ordenar()
    {
        var (useCase, _, _) = Montar();
        var (produtos, total) = await Listar(useCase, "receita90", page: 2, pageSize: 2);

        produtos.Select(p => p.Nome).Should().Equal("Agnolotti", "Farinha");
        total.Should().Be(6);
    }

    [Theory]
    [InlineData(SinalDoProduto.Parado, new[] { "Linguini" })]
    [InlineData(SinalDoProduto.NuncaVendeu, new[] { "Agnolotti", "Novo" })]
    [InlineData(SinalDoProduto.AbaixoDaTabela, new[] { "Lasanha" })]
    [InlineData(SinalDoProduto.SemCusto, new[] { "Agnolotti", "Linguini", "Novo" })]
    public async Task Sinal_filtra_os_mesmos_produtos_que_o_contador_da_faixa_conta(SinalDoProduto sinal, string[] esperados)
    {
        var (useCase, desempenho, _) = Montar();
        var resumo = (await desempenho.ExecuteAsync(Empresa)).Resumo;
        var (produtos, total) = await Listar(useCase, "nome", sinal, order: "asc");

        produtos.Select(p => p.Nome).Should().Equal(esperados);
        var contador = sinal switch
        {
            SinalDoProduto.Parado => resumo.Parados,
            SinalDoProduto.NuncaVendeu => resumo.NuncaVenderam,
            SinalDoProduto.AbaixoDaTabela => resumo.AbaixoDaTabela,
            _ => resumo.SemCusto,
        };
        total.Should().Be(contador);
    }

    [Fact]
    public async Task Situacao_e_categoria_valem_como_na_lista_padrao()
    {
        var (useCase, _, _) = Montar();

        (await Listar(useCase, "receita90", status: StatusProduto.Inativo)).Produtos.Select(p => p.Nome).Should().Equal("Molho");
        (await Listar(useCase, "receita90", categoriaId: Insumos)).Produtos.Select(p => p.Nome).Should().Equal("Farinha");
    }

    [Fact]
    public async Task Ordens_da_lista_padrao_tambem_valem_com_sinal()
    {
        var (useCase, _, _) = Montar();
        var (produtos, _) = await Listar(useCase, "nome", SinalDoProduto.NuncaVendeu, order: "desc");

        produtos.Select(p => p.Nome).Should().Equal("Novo", "Agnolotti");
    }

    [Fact]
    public async Task So_le_produtos_da_empresa_pedida()
    {
        var (useCase, _, repo) = Montar();
        await Listar(useCase, "receita90");

        await repo.Received(1).GetByIdsAsync(Empresa, Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().GetByIdsAsync(Arg.Is<Guid>(g => g != Empresa), Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ordem_desconhecida_e_recusada()
    {
        var (useCase, _, _) = Montar();
        var agir = () => Listar(useCase, "margem");

        await agir.Should().ThrowAsync<ArgumentException>();
    }
}
