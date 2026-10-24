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
        Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>(), publicadorEventos: Substitute.For<IPublicadorEventos>()), _produtos);

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

    // ── #1542: a receita é do prato e não sabe o tamanho da porção produzida. Avisa, não trava. ──

    [Fact]
    public async Task PorcaoProduzida_ReceitaQueRendeEmPorcoes_AvisaQueOInsumoNaoSegueOTamanho()
    {
        // Rende 6 com 1,2 kg de molho. 10 de 300 g ou 10 de 800 g baixam os mesmos 2 kg.
        var lasanha = Prato(marcado: true);
        var molho = Insumo("Molho", UnidadeMedida.G, 5000);
        Receita(lasanha, (molho, 1.2m, UnidadeMedida.Kg));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 10, null, "300 g")], "LOT-1", _data);

        Baixado(molho).Should().Be(2000m, "a conta não muda: só passa a avisar");
        avisos.Should().ContainSingle().Which.Should().Contain("Lasanha 300 g").And.Contain("tamanho")
            .And.Contain("rendimento da receita em peso", "#1552: o aviso diz como fazer a conta acompanhar o tamanho");
    }

    [Fact]
    public async Task PorcaoProduzida_ReceitaQueRendeEmPeso_AvisaQueAEmbalagemDesceuPeloPeso()
    {
        // Rende 3 kg com 6 bandejas. 10 porções de 300 g são 3 kg: descem 6 bandejas, e ela usou 10.
        var lasanha = Prato(marcado: false, rendimento: 3, unidade: UnidadeMedida.Kg);
        var bandeja = Insumo("Bandeja", UnidadeMedida.Un, 20);
        bandeja.EhEmbalagem = true;
        Receita(lasanha, (bandeja, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 10, 3000, "300 g")], "LOT-1", _data);

        Baixado(bandeja).Should().Be(6m, "a conta não muda: só passa a avisar");
        avisos.Should().ContainSingle().Which.Should().Contain("Lasanha 300 g").And.Contain("Bandeja").And.Contain("peso")
            .And.Contain("embalagem da porção no cardápio", "#1552: o aviso diz onde se escolhe a embalagem do tamanho");
    }

    [Theory]
    [InlineData(true, "300 g")]
    [InlineData(false, null)]
    public void SoPratoComMaisDeUmTamanho_LevaAPorcaoParaOAviso(bool variosTamanhos, string? esperada)
    {
        // Prato com uma porção só não tem outro tamanho com que a receita se confunda: sem aviso.
        var lasanha = Prato(marcado: true);
        var item = new RegistrarProducaoItemInput(lasanha.Id, Porcoes: 4, PesoPorPorcaoG: 300, ValidadeDias: 5, CustoUnitario: null,
            Porcao: "300 g", VariosTamanhos: variosTamanhos);

        var prato = PratoParaBaixa.Da(lasanha, item);

        prato.Porcao.Should().Be(esperada);
        (prato.Porcoes, prato.PesoProduzidoG).Should().Be((4, 1200));
    }

    [Fact]
    public async Task PorcaoProduzida_SoEmbalagemEmReceitaQueRendeEmPorcoes_NaoAvisa()
    {
        // 1 bandeja por porção vale para qualquer tamanho: a conta por porção está certa.
        var lasanha = Prato(marcado: false);
        var bandeja = Insumo("Bandeja", UnidadeMedida.Un, 20);
        bandeja.EhEmbalagem = true;
        Receita(lasanha, (bandeja, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 10, null, "300 g")], "LOT-1", _data);

        Baixado(bandeja).Should().Be(10m);
        avisos.Should().BeEmpty();
    }

    // ── M1.4e (#1552): a embalagem é da porção (uma por porção produzida) e a comida segue o peso produzido. ──

    private Produto Embalagem(string nome, UnidadeMedida unidade = UnidadeMedida.Un, bool marcada = true)
    {
        var p = Insumo(nome, unidade, 50);
        p.EhEmbalagem = marcada;
        _produtos.GetByIdAsync(_empresaId, p.Id).Returns(p);
        return p;
    }

    [Fact]
    public async Task DuasPorcoes_ReceitaEmPeso_InsumoSegueOPeso_EABandejaEADoTamanho()
    {
        // Aceite: rende 3 kg com 1,2 kg de molho. 10 de 300 g são 3 kg (1,2 kg de molho) e 10 de 800 g são
        // 8 kg (3,2 kg de molho). Cada porção desce a bandeja do tamanho dela, uma por porção.
        var lasanha = Prato(marcado: true, rendimento: 3, unidade: UnidadeMedida.Kg);
        var molho = Insumo("Molho", UnidadeMedida.G, 10000);
        var bandeja300 = Embalagem("Bandeja 300 g");
        var bandeja800 = Embalagem("Bandeja 800 g");
        Receita(lasanha, (molho, 1.2m, UnidadeMedida.Kg));

        var avisos = await Sut().BaixarAsync(_empresaId,
        [
            new PratoParaBaixa(lasanha, 10, 3000, "300 g", bandeja300.Id),
            new PratoParaBaixa(lasanha, 10, 8000, "800 g", bandeja800.Id),
        ], "LOT-1", _data);

        avisos.Should().BeEmpty("a conta acompanha o tamanho: o aviso da #1542 some");
        Baixado(molho).Should().Be(4400m);
        Baixado(bandeja300).Should().Be(10m);
        Baixado(bandeja800).Should().Be(10m);
    }

    [Fact]
    public async Task PorcaoComEmbalagemPropria_NaoUsaAEmbalagemDaReceita()
    {
        // Receita por peso com 6 bandejas genéricas para 3 kg. A porção com bandeja própria baixa só a dela.
        var lasanha = Prato(marcado: false, rendimento: 3, unidade: UnidadeMedida.Kg);
        var generica = Embalagem("Bandeja");
        var bandeja300 = Embalagem("Bandeja 300 g");
        Receita(lasanha, (generica, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 10, 3000, "300 g", bandeja300.Id)], "LOT-1", _data);

        avisos.Should().BeEmpty();
        Baixado(bandeja300).Should().Be(10m);
        Baixado(generica).Should().Be(0m, "a embalagem saiu da receita: vale a da porção");
    }

    [Fact]
    public async Task PorcaoComEmbalagemPropria_DesceMesmoEmPratoSemReceitaESemMarca()
    {
        // D-M2-03: embalagem desce em toda produção. A da porção não depende de receita nem da marca.
        var lasanha = Prato(marcado: false);
        var bandeja300 = Embalagem("Bandeja 300 g");
        Receita(lasanha);

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 4, null, null, bandeja300.Id)], "LOT-1", _data);

        avisos.Should().BeEmpty();
        Baixado(bandeja300).Should().Be(4m);
    }

    [Fact]
    public async Task PratoMarcadoSoComEmbalagemNaReceita_EPorcaoComEmbalagemPropria_NaoAvisaReceitaFaltando()
    {
        var lasanha = Prato(marcado: true);
        var generica = Embalagem("Bandeja");
        var bandeja300 = Embalagem("Bandeja 300 g");
        Receita(lasanha, (generica, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 6, null, "300 g", bandeja300.Id)], "LOT-1", _data);

        avisos.Should().BeEmpty("a receita existe; só não sobrou linha para baixar");
        Baixado(bandeja300).Should().Be(6m);
        Baixado(generica).Should().Be(0m);
    }

    [Theory]
    [InlineData(false, UnidadeMedida.Un, "não está mais marcada como embalagem")]
    [InlineData(true, UnidadeMedida.Kg, "não é contada por unidade")]
    public async Task EmbalagemDaPorcaoQueNaoServeMais_AvisaEUsaADaReceita(bool marcada, UnidadeMedida unidade, string motivo)
    {
        // Ela desmarcou a embalagem ou trocou a unidade em Insumos depois de escolher na porção: a baixa não some.
        var lasanha = Prato(marcado: false);
        var generica = Embalagem("Bandeja");
        var daPorcao = Embalagem("Bandeja antiga", unidade, marcada);
        Receita(lasanha, (generica, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 6, null, "300 g", daPorcao.Id)], "LOT-1", _data);

        avisos.Should().ContainSingle().Which.Should().Contain("Lasanha 300 g").And.Contain("Bandeja antiga").And.Contain(motivo);
        Baixado(generica).Should().Be(6m, "sem a embalagem da porção, vale a da receita");
        Baixado(daPorcao).Should().Be(0m);
    }

    [Fact]
    public async Task EmbalagemDaPorcaoTiradaDoEstoque_AvisaEUsaADaReceita()
    {
        // "Remover" um produto só o inativa. Sem olhar o status, a porção ficava sem embalagem nenhuma, e a
        // saída de produto inativo com saldo estoura dentro da transação da produção.
        var lasanha = Prato(marcado: false);
        var generica = Embalagem("Bandeja");
        var inativa = Embalagem("Bandeja 300 g");
        inativa.Status = StatusProduto.Inativo;
        Receita(lasanha, (generica, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 6, null, "300 g", inativa.Id)], "LOT-1", _data);

        avisos.Should().ContainSingle().Which.Should().Contain("Bandeja 300 g").And.Contain("fora do estoque");
        Baixado(generica).Should().Be(6m);
        Baixado(inativa).Should().Be(0m);
    }

    [Fact]
    public async Task EmbalagemDaPorcaoQueORepositorioNaoAcha_AvisaEUsaADaReceita()
    {
        var lasanha = Prato(marcado: false);
        var generica = Embalagem("Bandeja");
        Receita(lasanha, (generica, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 6, null, null, Guid.NewGuid())], "LOT-1", _data);

        avisos.Should().ContainSingle().Which.Should().Contain("Lasanha").And.Contain("não foi encontrada");
        Baixado(generica).Should().Be(6m);
    }

    [Fact]
    public void AEmbalagemDaPorcao_VaiDoItemProduzidoParaABaixa()
    {
        var lasanha = Prato(marcado: true);
        var embalagemId = Guid.NewGuid();
        var item = new RegistrarProducaoItemInput(lasanha.Id, Porcoes: 4, PesoPorPorcaoG: 300, ValidadeDias: 5, CustoUnitario: null,
            EmbalagemDaPorcaoId: embalagemId);

        PratoParaBaixa.Da(lasanha, item).EmbalagemDaPorcaoId.Should().Be(embalagemId);
    }

    [Fact]
    public async Task InsumoInativoComSaldo_AvisaENaoDerrubaAProducao_EOsOutrosInsumosBaixam()
    {
        // estoque-04 (#2003): inativar o insumo não zera o saldo. A saída de produto inativo lançava
        // ProdutoInativoException dentro da transação da produção e desfazia o lote inteiro.
        var lasanha = Prato(marcado: true);
        var molho = Insumo("Molho base", UnidadeMedida.G, 400);
        molho.Status = StatusProduto.Inativo;
        var bandeja = Insumo("Bandeja", UnidadeMedida.Un, 20);
        Receita(lasanha, (molho, 1.2m, UnidadeMedida.Kg), (bandeja, 6, UnidadeMedida.Un));

        var avisos = await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 12, null)], "LOT-1", _data);

        avisos.Should().ContainSingle().Which.Should().Contain("Molho base").And.Contain("inativo");
        Baixado(molho).Should().Be(0m, "produto inativo não tem saída");
        Baixado(bandeja).Should().Be(12m);
    }

    [Fact]
    public async Task SaidaDeInsumo_LevaOLoteNoDocumentoReferencia()
    {
        // estoque-21 (#2003): a baixa de insumo da produção era achada pelo texto da descrição, em quatro lugares.
        // O documento é a chave; a descrição fica para ler.
        var lasanha = Prato(marcado: true);
        var molho = Insumo("Molho", UnidadeMedida.G, 5000);
        Receita(lasanha, (molho, 1.2m, UnidadeMedida.Kg));

        await Sut().BaixarAsync(_empresaId, [new PratoParaBaixa(lasanha, 12, null)], "LOT-261009-AB", _data);

        _movimentos.Should().ContainSingle().Which.DocumentoReferencia.Should().Be("producao:LOT-261009-AB");
        BaixaDeInsumosDaProducao.DocumentoDaProducao("LOT-261009-AB").Should().Be("producao:LOT-261009-AB");
    }
}
