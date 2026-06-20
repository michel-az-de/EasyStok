using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront.Cardapio;

/// <summary>
/// Autoria de opções do item guarda-chuva (ADR-0035 / #652): Adicionar com opções e
/// reconciliação keyed-by-Id no Editar (update/insert/delete preservando Id).
/// </summary>
public class CardapioVariacaoAuthoringTests
{
    private readonly IStorefrontRepository _storefrontRepo = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapioRepo = Substitute.For<ICardapioItemRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    // ── Adicionar com opções ───────────────────────────────────────────

    [Fact]
    public async Task Adicionar_avulso_com_opcoes_persiste_variacoes_e_padrao()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-rav", "Rav", 0m);
        _storefrontRepo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);

        CardapioItem? capturado = null;
        await _cardapioRepo.AddAsync(Arg.Do<CardapioItem>(c => capturado = c), Arg.Any<CancellationToken>());

        var cmd = new AdicionarCardapioItemAdminCommand(
            s.Id, ProdutoId: null, NomePublico: "Ravioli de Abóbora", CategoriaTexto: "massas",
            OrdemExibicao: 1.0, Visivel: false, DescricaoPublica: null, Ingredientes: null,
            Alergenos: null, SugestaoMolho: null, TempoPreparo: null, FotoUrl: null,
            PrecoStorefront: 28m, Tag: null, PesoExibicao: null, FiltrosJson: null,
            EmpresaId: s.EmpresaId,
            Opcoes: new List<CardapioItemVariacaoInput>
            {
                new(Id: null, Rotulo: "300g", PrecoStorefront: 28m, EhPadrao: true, OrdemExibicao: 1),
                new(Id: null, Rotulo: "800g", PrecoStorefront: 42m, OrdemExibicao: 2),
            });

        await new AdicionarCardapioItemAdminUseCase(_storefrontRepo, _cardapioRepo, _produtoRepo, _uow)
            .ExecuteAsync(cmd);

        capturado.Should().NotBeNull();
        capturado!.Variacoes.Should().HaveCount(2);
        capturado.PrecoAPartirDe().Should().Be(28m, "opção mais barata disponível / padrão");
        capturado.VariacaoPadrao()!.Rotulo.Should().Be("300g");
        capturado.Variacoes.Count(v => v.EhPadrao).Should().Be(1, "≤1 padrão por item");
        await _uow.Received(1).CommitAsync();
    }

    // ── Editar: reconciliação keyed-by-Id ───────────────────────────────

    [Fact]
    public async Task Editar_reconcilia_keyed_by_id_update_insert_delete()
    {
        var item = ItemComOpcoes(out var pId, out var gId);
        _cardapioRepo.GetByIdAndScopeAsync(item.StorefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(item);

        // payload: mantém/atualiza P (id), remove G (ausente), insere M (sem id)
        var cmd = NewEdit(item, new List<CardapioItemVariacaoInput>
        {
            new(Id: pId, Rotulo: "P", PrecoStorefront: 25m, OrdemExibicao: 1),
            new(Id: null, Rotulo: "M", PrecoStorefront: 33m, OrdemExibicao: 2),
        });

        await new EditarCardapioItemAdminUseCase(_cardapioRepo, _uow).ExecuteAsync(cmd);

        item.Variacoes.Select(v => v.Rotulo).Should().BeEquivalentTo(new[] { "P", "M" });
        item.Variacoes.Single(v => v.Rotulo == "P").Id.Should().Be(pId, "update preserva o Id da opção");
        item.Variacoes.Single(v => v.Rotulo == "P").PrecoStorefront.Should().Be(25m, "atualizou o preço");
        item.Variacoes.Should().NotContain(v => v.Id == gId, "G ausente do payload foi removida");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Editar_opcoes_null_nao_mexe_nas_variacoes()
    {
        var item = ItemComOpcoes(out _, out _);
        _cardapioRepo.GetByIdAndScopeAsync(item.StorefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(item);

        var cmd = NewEdit(item, opcoes: null);   // Opcoes null = não tocar

        await new EditarCardapioItemAdminUseCase(_cardapioRepo, _uow).ExecuteAsync(cmd);

        item.Variacoes.Should().HaveCount(2, "Opcoes null preserva as opções existentes");
    }

    [Fact]
    public async Task Editar_troca_padrao_mantem_unico()
    {
        var item = ItemComOpcoes(out var pId, out var gId);
        item.DefinirVariacaoPadrao(pId);
        _cardapioRepo.GetByIdAndScopeAsync(item.StorefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(item);

        var cmd = NewEdit(item, new List<CardapioItemVariacaoInput>
        {
            new(Id: pId, Rotulo: "P", PrecoStorefront: 28m),
            new(Id: gId, Rotulo: "G", PrecoStorefront: 42m, EhPadrao: true),
        });

        await new EditarCardapioItemAdminUseCase(_cardapioRepo, _uow).ExecuteAsync(cmd);

        item.Variacoes.Count(v => v.EhPadrao).Should().Be(1);
        item.Variacoes.Single(v => v.Id == gId).EhPadrao.Should().BeTrue();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static CardapioItem ItemComOpcoes(out Guid pId, out Guid gId)
    {
        var item = CardapioItem.CriarAvulso(Guid.NewGuid(), "Ravioli", 30m, "massas");
        var p = CardapioItemVariacao.Criar(item.Id, "P", 28m, ordemExibicao: 1);
        var g = CardapioItemVariacao.Criar(item.Id, "G", 42m, ordemExibicao: 2);
        item.AdicionarVariacao(p);
        item.AdicionarVariacao(g);
        pId = p.Id;
        gId = g.Id;
        return item;
    }

    private static EditarCardapioItemAdminCommand NewEdit(CardapioItem item, IReadOnlyList<CardapioItemVariacaoInput>? opcoes) =>
        new(item.StorefrontId, item.Id,
            NomePublico: null, CategoriaTexto: null, DescricaoPublica: null, Ingredientes: null,
            Alergenos: null, SugestaoMolho: null, TempoPreparo: null, FotoUrl: null,
            PrecoStorefront: null, Tag: null, PesoExibicao: null, FiltrosJson: null,
            EmpresaId: null, Opcoes: opcoes);
}
