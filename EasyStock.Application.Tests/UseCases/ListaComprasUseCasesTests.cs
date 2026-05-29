using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ListasCompras;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Bateria de estabilidade do módulo de compras (listas) — o coração da feature
/// "lista que pensa" (gerar do estoque baixo) + ciclo de vida de itens. Cobre
/// entradas absurdas que não podem virar 500 e os caminhos null/idempotentes.
/// Antes desta bateria o módulo tinha ZERO testes de use case.
/// </summary>
public class ListaComprasUseCasesTests
{
    private readonly IListaComprasRepository _repo = Substitute.For<IListaComprasRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Guid _empresaId = Guid.NewGuid();

    private GerarListaComprasUseCase Gerar() =>
        new(_repo, _uow, Substitute.For<ILogger<GerarListaComprasUseCase>>());
    private CriarListaComprasUseCase Criar() =>
        new(_repo, _uow, Substitute.For<ILogger<CriarListaComprasUseCase>>());
    private AdicionarItemListaComprasUseCase Adicionar() =>
        new(_repo, _uow, Substitute.For<ILogger<AdicionarItemListaComprasUseCase>>());
    private ToggleItemListaComprasUseCase Toggle() =>
        new(_repo, _uow, Substitute.For<ILogger<ToggleItemListaComprasUseCase>>());
    private RemoverItemListaComprasUseCase Remover() =>
        new(_repo, _uow, Substitute.For<ILogger<RemoverItemListaComprasUseCase>>());
    private ArquivarListaComprasUseCase Arquivar() =>
        new(_repo, _uow, Substitute.For<ILogger<ArquivarListaComprasUseCase>>());
    private ReabrirListaComprasUseCase Reabrir() =>
        new(_repo, _uow, Substitute.For<ILogger<ReabrirListaComprasUseCase>>());

    // ── GerarLista (Fase 1: "gerar do estoque baixo") ──────────────────────────

    [Fact]
    public async Task Gerar_DeveCriarLista_ComItensValidos_EFiltrarTextosVazios()
    {
        ListaCompras? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ListaCompras>())).Do(ci => salvo = ci.Arg<ListaCompras>());

        var result = await Gerar().ExecuteAsync(new GerarListaComprasCommand(
            _empresaId, "Reposição", new[]
            {
                new GerarItemListaComprasInput("Arroz",  Quantidade: 3m, Unidade: "sc"),
                new GerarItemListaComprasInput("Feijão", Quantidade: 2m),
                new GerarItemListaComprasInput("   "), // deve ser filtrado
            }));

        salvo.Should().NotBeNull();
        salvo!.Itens.Should().HaveCount(2);
        result.TotalItens.Should().Be(2);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Gerar_DevePreservarProdutoId_NosItensGerados()
    {
        // Vínculo com o catálogo (Fase 2): item gerado carrega o ProdutoId.
        ListaCompras? salvo = null;
        _repo.When(r => r.AddAsync(Arg.Any<ListaCompras>())).Do(ci => salvo = ci.Arg<ListaCompras>());
        var produtoId = Guid.NewGuid();

        await Gerar().ExecuteAsync(new GerarListaComprasCommand(
            _empresaId, "Reposição", new[]
            {
                new GerarItemListaComprasInput("Farinha 00", ProdutoId: produtoId, Quantidade: 5m),
            }));

        salvo!.Itens.Should().ContainSingle();
        salvo.Itens.First().ProdutoId.Should().Be(produtoId);
    }

    [Fact]
    public async Task Gerar_DeveLancarValidation_QuandoNenhumItemValido()
    {
        var act = () => Gerar().ExecuteAsync(new GerarListaComprasCommand(
            _empresaId, "Reposição", new[] { new GerarItemListaComprasInput("  "), new GerarItemListaComprasInput("") }));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*item*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Gerar_DeveLancarValidation_QuandoListaSemItens()
    {
        var act = () => Gerar().ExecuteAsync(new GerarListaComprasCommand(
            _empresaId, "Reposição", Array.Empty<GerarItemListaComprasInput>()));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Gerar_DeveLancarValidation_QuandoNomeVazio()
    {
        var act = () => Gerar().ExecuteAsync(new GerarListaComprasCommand(
            _empresaId, "   ", new[] { new GerarItemListaComprasInput("Arroz") }));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*ome*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Gerar_DeveLancarValidation_QuandoEmpresaIdVazio()
    {
        var act = () => Gerar().ExecuteAsync(new GerarListaComprasCommand(
            Guid.Empty, "Reposição", new[] { new GerarItemListaComprasInput("Arroz") }));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    // ── CriarLista (em branco) ─────────────────────────────────────────────────

    [Fact]
    public async Task Criar_DeveCriarLista_Simples()
    {
        await Criar().ExecuteAsync(new CriarListaComprasCommand(_empresaId, "Feira da semana"));

        await _repo.Received(1).AddAsync(Arg.Any<ListaCompras>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Criar_DeveLancarValidation_QuandoNomeVazio()
    {
        var act = () => Criar().ExecuteAsync(new CriarListaComprasCommand(_empresaId, "  "));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    // ── AdicionarItem ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Adicionar_DeveLancarValidation_QuandoTextoVazio()
    {
        var act = () => Adicionar().ExecuteAsync(new AdicionarItemListaComprasCommand(
            _empresaId, Guid.NewGuid(), Texto: "   "));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Adicionar_DeveRetornarNull_QuandoListaInexistente()
    {
        // repo.GetByIdAsync não configurado → null.
        var result = await Adicionar().ExecuteAsync(new AdicionarItemListaComprasCommand(
            _empresaId, Guid.NewGuid(), Texto: "Sal"));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Adicionar_DeveLancarValidation_QuandoListaArquivada()
    {
        var lista = ListaCompras.Criar(_empresaId, "Feira");
        lista.Arquivar();
        _repo.GetByIdAsync(_empresaId, lista.Id).Returns(lista);

        var act = () => Adicionar().ExecuteAsync(new AdicionarItemListaComprasCommand(
            _empresaId, lista.Id, Texto: "Sal"));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*arquivada*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Adicionar_DeveAdicionarItem_Happy()
    {
        var lista = ListaCompras.Criar(_empresaId, "Feira");
        _repo.GetByIdAsync(_empresaId, lista.Id).Returns(lista);
        ItemListaCompras? item = null;
        _repo.When(r => r.AddItemAsync(Arg.Any<ItemListaCompras>())).Do(ci => item = ci.Arg<ItemListaCompras>());

        var result = await Adicionar().ExecuteAsync(new AdicionarItemListaComprasCommand(
            _empresaId, lista.Id, Texto: "  Sal grosso  ", Quantidade: 2m, Unidade: "kg"));

        result.Should().NotBeNull();
        item.Should().NotBeNull();
        item!.Texto.Should().Be("Sal grosso"); // trim
        await _uow.Received(1).CommitAsync();
    }

    // ── ToggleItem ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Toggle_DeveRetornarNull_QuandoItemDeOutraLista()
    {
        var lista = ListaCompras.Criar(_empresaId, "Feira");
        _repo.GetByIdAsync(_empresaId, lista.Id).Returns(lista);
        var item = new ItemListaCompras { Id = Guid.NewGuid(), ListaComprasId = Guid.NewGuid(), Texto = "x" };
        _repo.GetItemAsync(item.Id).Returns(item);

        var result = await Toggle().ExecuteAsync(new ToggleItemListaComprasCommand(
            _empresaId, lista.Id, item.Id, Done: true));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Toggle_DeveMarcarDone_QuandoDoneTrue()
    {
        var lista = ListaCompras.Criar(_empresaId, "Feira");
        _repo.GetByIdAsync(_empresaId, lista.Id).Returns(lista);
        var item = new ItemListaCompras { Id = Guid.NewGuid(), ListaComprasId = lista.Id, Texto = "x" };
        _repo.GetItemAsync(item.Id).Returns(item);

        var result = await Toggle().ExecuteAsync(new ToggleItemListaComprasCommand(
            _empresaId, lista.Id, item.Id, Done: true, UsuarioId: Guid.NewGuid(), UsuarioNome: "Op"));

        result.Should().NotBeNull();
        item.Done.Should().BeTrue();
        item.DoneEm.Should().NotBeNull();
        await _uow.Received(1).CommitAsync();
    }

    // ── RemoverItem ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remover_DeveRetornarFalse_QuandoItemInexistente()
    {
        var lista = ListaCompras.Criar(_empresaId, "Feira");
        _repo.GetByIdAsync(_empresaId, lista.Id).Returns(lista);
        // GetItemAsync não configurado → null.

        var ok = await Remover().ExecuteAsync(new RemoverItemListaComprasCommand(
            _empresaId, lista.Id, Guid.NewGuid()));

        ok.Should().BeFalse();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Remover_DeveRemover_Happy()
    {
        var lista = ListaCompras.Criar(_empresaId, "Feira");
        _repo.GetByIdAsync(_empresaId, lista.Id).Returns(lista);
        var item = new ItemListaCompras { Id = Guid.NewGuid(), ListaComprasId = lista.Id, Texto = "x" };
        _repo.GetItemAsync(item.Id).Returns(item);

        var ok = await Remover().ExecuteAsync(new RemoverItemListaComprasCommand(
            _empresaId, lista.Id, item.Id));

        ok.Should().BeTrue();
        await _repo.Received(1).RemoveItemAsync(item.Id);
        await _uow.Received(1).CommitAsync();
    }

    // ── Arquivar / Reabrir (idempotência + null) ───────────────────────────────

    [Fact]
    public async Task Arquivar_DeveRetornarNull_QuandoListaInexistente()
    {
        var result = await Arquivar().ExecuteAsync(new ArquivarListaComprasCommand(_empresaId, Guid.NewGuid()));

        result.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Reabrir_DeveSerIdempotente_QuandoListaJaAberta()
    {
        var lista = ListaCompras.Criar(_empresaId, "Feira"); // já "aberta"
        _repo.GetByIdWithItemsAsync(_empresaId, lista.Id).Returns(lista);

        var result = await Reabrir().ExecuteAsync(new ReabrirListaComprasCommand(_empresaId, lista.Id));

        result.Should().NotBeNull();
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<ListaCompras>());
        await _uow.DidNotReceive().CommitAsync();
    }
}
