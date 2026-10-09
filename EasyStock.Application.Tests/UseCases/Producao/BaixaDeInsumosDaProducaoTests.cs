using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Producao;

/// <summary>D-M2-01 (#1499): baixa de insumo pela receita só para prato marcado; falta avisa, não trava.</summary>
public class BaixaDeInsumosDaProducaoTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly DateTime _data = new(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc);
    private readonly IProdutoComposicaoRepository _composicao = Substitute.For<IProdutoComposicaoRepository>();
    private readonly IProdutoRepository _produtos = Substitute.For<IProdutoRepository>();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();
    private readonly IVendaRepository _vendas = Substitute.For<IVendaRepository>();
    private readonly IItemVendaRepository _itensVenda = Substitute.For<IItemVendaRepository>();
    private readonly IMovimentacaoEstoqueRepository _movs = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<MovimentacaoEstoque> _movimentos = [];

    public BaixaDeInsumosDaProducaoTests()
    {
        _uow.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        _movs.GetTaxaSaidaDiariaAsync(_empresaId, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(0m);
        _movs.When(m => m.InsertRangeAsync(Arg.Any<IEnumerable<MovimentacaoEstoque>>()))
            .Do(c => _movimentos.AddRange(c.Arg<IEnumerable<MovimentacaoEstoque>>()));
        _itens.GetLotesDisponiveisParaSaidaAsync(_empresaId, Arg.Any<Guid>(), null, Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(Array.Empty<ItemEstoque>());
    }

    private BaixaDeInsumosDaProducao Sut() => new(_composicao, _itens, new RegistrarSaidaEstoqueUseCase(
        _produtos, _itens, _vendas, _itensVenda, _movs, _uow,
        Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>(), publicadorEventos: Substitute.For<IPublicadorEventos>()));

    private Produto Prato(bool marcado, decimal rendimento = 6, UnidadeMedida unidade = UnidadeMedida.Un) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Lasanha", Status = StatusProduto.Ativo,
        RendimentoBase = rendimento, RendimentoUnidade = unidade, BaixaInsumoAutomatica = marcado,
    };

    private Produto Insumo(string nome, UnidadeMedida unidade, params decimal[] saldos)
    {
        var p = new Produto { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = nome, Status = StatusProduto.Ativo, UnidadeMedidaBase = unidade, EhInsumo = true };
        _produtos.GetByIdAsync(p.Id).Returns(p);
        _itens.GetLotesDisponiveisParaSaidaAsync(_empresaId, p.Id, null, Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(saldos.Select((s, i) => new ItemEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoId = p.Id,
                QuantidadeAtual = Quantidade.From(s), QuantidadeInicial = Quantidade.From(s),
                CustoUnitario = Dinheiro.FromDecimal(0m), Status = StatusItemEstoque.Ok, EntradaEm = _data.AddDays(-10 + i),
            }).ToArray());
        return p;
    }

    private void Receita(Produto prato, params (Produto Insumo, decimal Qtd, UnidadeMedida Unidade)[] linhas) =>
        _composicao.GetByProdutoFinalAsync(_empresaId, prato.Id, null, Arg.Any<CancellationToken>()).Returns(
            linhas.Select((l, i) => new ProdutoComposicao
            {
                Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoFinalId = prato.Id, InsumoId = l.Insumo.Id,
                Insumo = l.Insumo, Quantidade = l.Qtd, Unidade = l.Unidade, OrdemExibicao = i,
            }).ToList());

    private decimal Baixado(Produto insumo) => _movimentos.Where(m => m.ProdutoId == insumo.Id).Sum(m => m.Quantidade.Value);

    [Fact]
    public async Task PratoMarcado_BaixaCadaInsumoConvertido_ComoUsoInterno()
    {
        // Rende 6: 1,2 kg de molho e 6 bandejas. Produziu 12 porções → 2.400 g de molho e 12 bandejas.
        var lasanha = Prato(marcado: true);
        var molho = Insumo("Molho", UnidadeMedida.G, 5000);
        var bandeja = Insumo("Bandeja", UnidadeMedida.Un, 20);
        Receita(lasanha, (molho, 1.2m, UnidadeMedida.Kg), (bandeja, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 12, null)], "LOT-261009-AB", _data);

        avisos.Should().BeEmpty();
        Baixado(molho).Should().Be(2400m);
        Baixado(bandeja).Should().Be(12m);
        _movimentos.Should().OnlyContain(m => m.Natureza == NaturezaMovimentacaoEstoque.UsoInterno);
        await _vendas.Received(1).InsertAsync(Arg.Is<Venda>(v => v.Observacoes!.Contains("LOT-261009-AB")));
    }

    [Fact]
    public async Task PratoSemMarca_NaoBaixaNada()
    {
        var lasanha = Prato(marcado: false);
        Receita(lasanha, (Insumo("Molho", UnidadeMedida.G, 5000), 1.2m, UnidadeMedida.Kg));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 12, null)], "LOT-1", _data);

        avisos.Should().BeEmpty();
        _movimentos.Should().BeEmpty("sem a marca, só embalagem desce, e esta receita não tem embalagem");
    }

    [Fact]
    public async Task Embalagem_DesceMesmoSemAMarca_EORestoDaReceitaNao()
    {
        // Aceite M2.7 (#1523, D-M2-03): rende 6 com 6 bandejas (1 por porção); produziu 6 → 6 bandejas.
        var lasanha = Prato(marcado: false);
        var molho = Insumo("Molho", UnidadeMedida.G, 5000);
        var bandeja = Insumo("Bandeja 800 g", UnidadeMedida.Un, 20);
        bandeja.EhEmbalagem = true;
        Receita(lasanha, (molho, 1.2m, UnidadeMedida.Kg), (bandeja, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 6, null)], "LOT-1", _data);

        avisos.Should().BeEmpty();
        Baixado(bandeja).Should().Be(6m);
        Baixado(molho).Should().Be(0m, "o prato não está marcado para baixar insumos (D-M2-01)");
    }

    [Fact]
    public async Task PratoSemMarcaEsemReceita_NaoAvisa()
    {
        var lasanha = Prato(marcado: false);
        Receita(lasanha);

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 6, null)], "LOT-1", _data);

        avisos.Should().BeEmpty("o aviso de receita faltando é só para quem pediu a baixa");
    }

    [Fact]
    public async Task FaltaParcial_AvisaEFicaDescoberto_SemTravar()
    {
        var lasanha = Prato(marcado: true);
        var molho = Insumo("Molho", UnidadeMedida.G, 1000);
        Receita(lasanha, (molho, 1.2m, UnidadeMedida.Kg));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 12, null)], "LOT-1", _data);

        avisos.Should().ContainSingle().Which.Should().Contain("Faltou 1400 G de Molho");
        Baixado(molho).Should().Be(2400m, "a saída sai inteira; a falta vira descoberto no lote (#540)");
    }

    [Fact]
    public async Task SemSaldoNenhum_SoAvisa_EOsOutrosInsumosBaixam()
    {
        var lasanha = Prato(marcado: true);
        var molho = Insumo("Molho", UnidadeMedida.G);
        var bandeja = Insumo("Bandeja", UnidadeMedida.Un, 20);
        Receita(lasanha, (molho, 1.2m, UnidadeMedida.Kg), (bandeja, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 12, null)], "LOT-1", _data);

        avisos.Should().ContainSingle().Which.Should().StartWith("Sem estoque de Molho");
        Baixado(molho).Should().Be(0m);
        Baixado(bandeja).Should().Be(12m);
    }

    [Fact]
    public async Task ReceitaQueRendeEmKg_UsaOPesoProduzido()
    {
        // Rende 3 kg com 600 g de ricota; produziu 1,5 kg → metade da receita = 300 g.
        var nhoque = Prato(marcado: true, rendimento: 3, unidade: UnidadeMedida.Kg);
        var ricota = Insumo("Ricota", UnidadeMedida.G, 5000);
        Receita(nhoque, (ricota, 600, UnidadeMedida.G));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(nhoque, 5, 1500)], "LOT-1", _data);

        avisos.Should().BeEmpty();
        Baixado(ricota).Should().Be(300m);
    }

    [Fact]
    public async Task ReceitaEmKgSemPeso_AvisaENaoBaixa()
    {
        var nhoque = Prato(marcado: true, rendimento: 3, unidade: UnidadeMedida.Kg);
        var ricota = Insumo("Ricota", UnidadeMedida.G, 5000);
        Receita(nhoque, (ricota, 600, UnidadeMedida.G));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(nhoque, 5, null)], "LOT-1", _data);

        avisos.Should().ContainSingle().Which.Should().Contain("sem peso");
        _movimentos.Should().BeEmpty();
    }

    [Fact]
    public async Task MesmoInsumoEmDoisPratos_SomaNumaSaidaSo()
    {
        var lasanha = Prato(marcado: true);
        var canelone = Prato(marcado: true);
        var molho = Insumo("Molho", UnidadeMedida.G, 5000);
        Receita(lasanha, (molho, 600, UnidadeMedida.G));
        Receita(canelone, (molho, 300, UnidadeMedida.G));

        await Sut().BaixarAsync(_empresaId,
            [new PratoParaBaixa(lasanha, 6, null), new PratoParaBaixa(canelone, 12, null)], "LOT-1", _data);

        Baixado(molho).Should().Be(1200m);
        _movimentos.Should().ContainSingle();
    }
}
