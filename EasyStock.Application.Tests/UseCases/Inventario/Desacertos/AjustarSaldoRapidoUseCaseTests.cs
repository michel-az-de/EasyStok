using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inventario.Desacertos;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Tests.UseCases.Inventario.Desacertos;

/// <summary>S22 (#1181): ajuste rápido reusa a contagem (<c>ItemEstoque.AplicarAjusteContagem</c>) sem sessão completa.</summary>
public class AjustarSaldoRapidoUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _produtoId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();
    private readonly IContagemRepository _contagens = Substitute.For<IContagemRepository>();
    private readonly IMovimentacaoEstoqueRepository _movs = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IOperacaoEventPublisher _eventos = Substitute.For<IOperacaoEventPublisher>();

    private AjustarSaldoRapidoUseCase Criar() => new(_itens, _contagens, _movs, _uow, _eventos, TimeProvider.System);

    private ItemEstoque Lote(decimal atual, decimal descoberto = 0m, int diasAtras = 1) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoId = _produtoId,
        QuantidadeInicial = Quantidade.From(10), QuantidadeAtual = Quantidade.From(atual),
        QuantidadeDescoberta = Quantidade.From(descoberto),
        CustoUnitario = Dinheiro.FromDecimal(4m), Status = StatusItemEstoque.Ok,
        EntradaEm = DateTime.UtcNow.AddDays(-diasAtras), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
    };

    private AjustarSaldoRapidoInput Input(decimal contada, string motivo = "contei na geladeira") =>
        new(_empresaId, _usuarioId, _produtoId, null, contada, motivo);

    [Fact]
    public async Task ZeraDescobertoEGravaAjuste()
    {
        var lote = Lote(atual: 0m, descoberto: 2m);
        _itens.GetLotesParaAjusteAsync(_empresaId, _produtoId, null, Arg.Any<CancellationToken>()).Returns(new[] { lote });
        AjusteInventario? ajuste = null;
        Contagem? contagem = null;
        await _contagens.AddAjusteAsync(Arg.Do<AjusteInventario>(a => ajuste = a), Arg.Any<CancellationToken>());
        await _contagens.AddAsync(Arg.Do<Contagem>(c => contagem = c));

        var r = await Criar().ExecuteAsync(Input(5m));

        r.QuantidadeAtual.Should().Be(5m);
        r.QuantidadeDescoberta.Should().Be(0m);
        lote.QuantidadeAtual.Value.Should().Be(5m);
        lote.QuantidadeDescoberta.Value.Should().Be(0m);
        contagem.Should().NotBeNull();
        contagem!.Status.Should().Be(StatusContagem.Aplicada);
        contagem.Observacao.Should().Contain("contei na geladeira");
        ajuste.Should().NotBeNull();
        ajuste!.ContagemId.Should().Be(contagem.Id);
        ajuste.Linhas.Should().ContainSingle(l => l.Delta == 5m && l.Tipo == TipoAjusteLinha.Sobra);
        await _movs.Received(1).InsertRangeAsync(Arg.Is<IEnumerable<MovimentacaoEstoque>>(ms =>
            ms.Count() == 1 && ms.All(m => m.Natureza == NaturezaMovimentacaoEstoque.Ajuste && m.Quantidade.Value == 5m)));
        await _uow.Received(1).CommitAsync();
        await _eventos.Received(1).PublicarAsync(EventosOperacao.EstoqueDesacertoResolvido, _empresaId,
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemDescobertoAjustaNormal()
    {
        var antigo = Lote(atual: 3m, diasAtras: 5);
        var novo = Lote(atual: 4m, diasAtras: 1);
        _itens.GetLotesParaAjusteAsync(_empresaId, _produtoId, null, Arg.Any<CancellationToken>()).Returns(new[] { novo, antigo });

        var r = await Criar().ExecuteAsync(Input(5m, "quebrou dois"));

        r.QuantidadeAtual.Should().Be(5m);
        antigo.QuantidadeAtual.Value.Should().Be(3m);
        novo.QuantidadeAtual.Value.Should().Be(2m);
        await _movs.Received(1).InsertRangeAsync(Arg.Is<IEnumerable<MovimentacaoEstoque>>(ms =>
            ms.Count() == 1 && ms.Single().Tipo == TipoMovimentacaoEstoque.Saida));
        await _eventos.DidNotReceive().PublicarAsync(EventosOperacao.EstoqueDesacertoResolvido, Arg.Any<Guid>(),
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContagemZeroNuncaDeixaSaldoNegativo()
    {
        var lote = Lote(atual: 3m, descoberto: 1m);
        _itens.GetLotesParaAjusteAsync(_empresaId, _produtoId, null, Arg.Any<CancellationToken>()).Returns(new[] { lote });

        var r = await Criar().ExecuteAsync(Input(0m));

        r.QuantidadeAtual.Should().Be(0m);
        r.QuantidadeDescoberta.Should().Be(0m);
        lote.QuantidadeAtual.Value.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public async Task ProdutoSemLoteRecusa()
    {
        _itens.GetLotesParaAjusteAsync(_empresaId, _produtoId, null, Arg.Any<CancellationToken>()).Returns(Array.Empty<ItemEstoque>());
        Func<Task> act = () => Criar().ExecuteAsync(Input(5m));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task MotivoObrigatorio()
    {
        Func<Task> act = () => Criar().ExecuteAsync(Input(5m, " "));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
