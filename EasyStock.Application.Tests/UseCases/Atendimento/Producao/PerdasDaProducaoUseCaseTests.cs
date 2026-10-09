using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Producao;

/// <summary>M2.6 (#1511): perda com motivo (D-M2-02), custo do lote, limite do Gerente e resumo sem ajuste.</summary>
public class PerdasDaProducaoUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    // 09/10/2026 12:00 em Brasília (15:00 UTC).
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero));
    private readonly IProdutoRepository _produtos = Substitute.For<IProdutoRepository>();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();
    private readonly IVendaRepository _vendas = Substitute.For<IVendaRepository>();
    private readonly IItemVendaRepository _itensVenda = Substitute.For<IItemVendaRepository>();
    private readonly IMovimentacaoEstoqueRepository _movs = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<MovimentacaoEstoque> _gravadas = [];
    private readonly Produto _lasanha;

    public PerdasDaProducaoUseCaseTests()
    {
        _uow.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        _movs.GetTaxaSaidaDiariaAsync(_empresaId, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(0m);
        _movs.When(m => m.InsertRangeAsync(Arg.Any<IEnumerable<MovimentacaoEstoque>>()))
            .Do(c => _gravadas.AddRange(c.Arg<IEnumerable<MovimentacaoEstoque>>()));
        _lasanha = new Produto { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Lasanha", Status = StatusProduto.Ativo };
        _produtos.GetByIdAsync(_lasanha.Id).Returns(_lasanha);
    }

    private PerdasDaProducaoUseCase Sut() => new(_itens, _movs, new RegistrarSaidaEstoqueUseCase(
        _produtos, _itens, _vendas, _itensVenda, _movs, _uow,
        Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>(), publicadorEventos: Substitute.For<IPublicadorEventos>()), _relogio);

    private ItemEstoque Lote(decimal saldo, decimal custo, DateTime validade, string codigo)
    {
        var lote = new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoId = _lasanha.Id, Produto = _lasanha,
            QuantidadeAtual = Quantidade.From(saldo), QuantidadeInicial = Quantidade.From(saldo),
            CustoUnitario = Dinheiro.FromDecimal(custo), ValidadeEm = Validade.From(validade),
            CodigoLote = CodigoLote.From(codigo), Status = StatusItemEstoque.Ok, EntradaEm = new DateTime(2026, 10, 1),
        };
        _itens.GetByIdAsync(_empresaId, lote.Id).Returns(lote);
        _itens.GetByIdComLockAsync(_empresaId, lote.Id).Returns(lote);
        return lote;
    }

    private void Fefo(bool comVencidos, params ItemEstoque[] lotes) =>
        _itens.GetLotesDisponiveisParaSaidaAsync(_empresaId, _lasanha.Id, null, true, comVencidos).Returns(lotes);

    [Fact]
    public async Task PerdaNoPreparo_SaiPorFefo_EGravaOCustoDeCadaLote()
    {
        var antigo = Lote(1, 4m, new DateTime(2026, 10, 12), "LOT-A");
        var novo = Lote(5, 6m, new DateTime(2026, 10, 15), "LOT-B");
        Fefo(true, antigo, novo);

        var r = await Sut().LancarAsync(_empresaId, false, new LancarPerdaInput(_lasanha.Id, null, 2, MotivoPerda.PerdaNoPreparo));

        r.Valor.Should().Be(10m, "1 × R$ 4 do lote antigo + 1 × R$ 6 do novo");
        _gravadas.Select(m => (m.ItemEstoqueId, m.Quantidade.Value, m.ValorUnitario!.Valor)).Should().Equal((antigo.Id, 1m, 4m), (novo.Id, 1m, 6m));
        _gravadas.Should().OnlyContain(m => m.Natureza == NaturezaMovimentacaoEstoque.Perda && m.Descricao == "Perda · Perda no preparo");
        (antigo.QuantidadeAtual.Value, novo.QuantidadeAtual.Value).Should().Be((0m, 4m));
    }

    [Fact]
    public async Task AcimaDe50_SemGerente_Recusa_ComGerente_Lanca()
    {
        var lote = Lote(10, 12m, new DateTime(2026, 10, 15), "LOT-A");
        Fefo(true, lote);
        var input = new LancarPerdaInput(_lasanha.Id, null, 5, MotivoPerda.PerdaNoPreparo);

        var semGerente = () => Sut().LancarAsync(_empresaId, false, input);
        (await semGerente.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be(PerdasDaProducaoUseCase.CodigoExigeGerente);
        _gravadas.Should().BeEmpty();

        (await Sut().LancarAsync(_empresaId, true, input)).Valor.Should().Be(60m);
    }

    [Fact]
    public async Task Outro_ExigeTexto_EVaiComoPrejuizo()
    {
        Fefo(true, Lote(5, 4m, new DateTime(2026, 10, 15), "LOT-A"));

        var semTexto = () => Sut().LancarAsync(_empresaId, false, new LancarPerdaInput(_lasanha.Id, null, 1, MotivoPerda.Outro, " "));
        await semTexto.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*escreva o motivo*");

        await Sut().LancarAsync(_empresaId, false, new LancarPerdaInput(_lasanha.Id, null, 1, MotivoPerda.Outro, "caiu no chão"));
        _gravadas.Should().ContainSingle().Which.Should().Match<MovimentacaoEstoque>(m =>
            m.Natureza == NaturezaMovimentacaoEstoque.Prejuizo && m.Descricao == "Perda · Outro: caiu no chão");
    }

    [Fact]
    public async Task MaisDoQueOSaldo_Recusa_SemCriarDescoberto()
    {
        Fefo(true, Lote(2, 4m, new DateTime(2026, 10, 15), "LOT-A"));

        var act = () => Sut().LancarAsync(_empresaId, false, new LancarPerdaInput(_lasanha.Id, null, 3, MotivoPerda.PerdaNoPreparo));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("Só há 2 em estoque*");
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task LoteVencido_NaoSeDoa_MasSaiComoVencido()
    {
        var vencido = Lote(3, 4m, new DateTime(2026, 10, 7), "LOT-V");

        var doar = () => Sut().LancarAsync(_empresaId, false, new LancarPerdaInput(_lasanha.Id, vencido.Id, 3, MotivoPerda.Doacao));
        await doar.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*lance como Vencido*");

        var r = await Sut().LancarAsync(_empresaId, false, new LancarPerdaInput(_lasanha.Id, vencido.Id, 3, MotivoPerda.Vencido));
        r.Valor.Should().Be(12m);
        _gravadas.Should().ContainSingle().Which.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Vencimento);
    }

    private MovimentacaoEstoque Mov(NaturezaMovimentacaoEstoque natureza, decimal qtd, decimal valor, string descricao, bool estornada = false) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoId = _lasanha.Id, Produto = _lasanha,
        Tipo = TipoMovimentacaoEstoque.Saida, Natureza = natureza, Quantidade = Quantidade.From(qtd),
        ValorTotal = Dinheiro.FromDecimal(valor), Descricao = descricao, DataMovimentacao = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
        EstornadaEm = estornada ? new DateTime(2026, 10, 8, 13, 0, 0, DateTimeKind.Utc) : null,
    };

    [Fact]
    public async Task Resumo_PorMotivoEPrato_SemBaixaDeInsumoNemDesfeitas_ESemAjuste()
    {
        IReadOnlyCollection<NaturezaMovimentacaoEstoque>? pedidas = null;
        _movs.GetSaidasPorNaturezaAsync(_empresaId, Arg.Do<IReadOnlyCollection<NaturezaMovimentacaoEstoque>>(n => pedidas = n),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                Mov(NaturezaMovimentacaoEstoque.Perda, 2, 10m, "Perda · Perda no preparo"),
                Mov(NaturezaMovimentacaoEstoque.UsoInterno, 1, 3m, "Perda · Degustação"),
                Mov(NaturezaMovimentacaoEstoque.UsoInterno, 600, 18m, "Insumo da produção LOT-261009-001"),
                Mov(NaturezaMovimentacaoEstoque.Perda, 4, 20m, "Perda · Perda no preparo", estornada: true),
            ]);

        var r = await Sut().ResumoAsync(_empresaId);

        (r.De, r.Ate).Should().Be((new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 9)), "padrão: os últimos 7 dias");
        r.Valor.Should().Be(13m);
        r.PorMotivo.Select(p => (p.Motivo, p.Valor)).Should().Equal((MotivoPerda.PerdaNoPreparo, 10m), (MotivoPerda.Degustacao, 3m));
        r.PorProduto.Should().ContainSingle().Which.Quantidade.Should().Be(3m);
        r.Lancamentos.Should().HaveCount(3).And.ContainSingle(l => l.Desfeita);
        pedidas.Should().NotContain(NaturezaMovimentacaoEstoque.Ajuste).And.NotContain(NaturezaMovimentacaoEstoque.Venda);
    }

    [Fact]
    public async Task Vencidos_SoOQueJaVenceuNoDiaOperacional()
    {
        var ontem = Lote(3, 4m, new DateTime(2026, 10, 8), "LOT-ONTEM");
        var hoje = Lote(2, 4m, new DateTime(2026, 10, 9), "LOT-HOJE");
        _itens.GetComSaldoEValidadeAteAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([ontem, hoje]);

        var r = await Sut().VencidosAsync(_empresaId);

        var v = r.Should().ContainSingle("vence hoje ainda vale hoje").Subject;
        (v.Lote, v.DiasVencido, v.Valor).Should().Be(("LOT-ONTEM", 1, 12m));
        _gravadas.Should().BeEmpty("sugestão não lança sozinha");
    }
}
