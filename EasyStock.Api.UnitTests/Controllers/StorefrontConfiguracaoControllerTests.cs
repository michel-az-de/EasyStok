using System.Reflection;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.AtivarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.DesativarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.EditarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.ObterStorefrontAdmin;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// P01-B (#1173/#1176): configuração da vitrine pelo próprio tenant. A vitrine sai sempre da
/// empresa do token; nem a rota nem o corpo carregam id de vitrine ou de empresa.
/// </summary>
public class StorefrontConfiguracaoControllerTests
{
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ILojaRepository _lojas = Substitute.For<ILojaRepository>();
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private readonly Guid _empresaA = Guid.NewGuid();
    private readonly Guid _empresaB = Guid.NewGuid();
    private readonly StorefrontEntity _vitrineA;
    private readonly StorefrontEntity _vitrineB;
    private readonly StorefrontConfiguracaoController _controller;

    public StorefrontConfiguracaoControllerTests()
    {
        _vitrineA = StorefrontEntity.Criar(_empresaA, "loja-a", "Loja A", 0m);
        _vitrineB = StorefrontEntity.Criar(_empresaB, "loja-b", "Loja B", 0m);

        _storefronts.GetByEmpresaAsync(_empresaA).Returns(_vitrineA);
        _storefronts.GetByEmpresaAsync(_empresaB).Returns(_vitrineB);
        _storefronts.GetByIdAsync(_vitrineA.Id).Returns(_vitrineA);
        _storefronts.GetByIdAsync(_vitrineB.Id).Returns(_vitrineB);

        _currentUser.EmpresaId.Returns(_empresaA);

        _controller = new StorefrontConfiguracaoController(
            _storefronts,
            new ObterStorefrontAdminUseCase(_storefronts, _empresas, _cardapio),
            new EditarStorefrontAdminUseCase(_storefronts, _lojas, _uow),
            new AtivarStorefrontAdminUseCase(_storefronts, _uow),
            new DesativarStorefrontAdminUseCase(_storefronts, _uow),
            _currentUser);
    }

    private static StorefrontConfiguracaoController.EditarConfiguracaoVitrineRequest Pedido(
        string? subtitulo = null, string? dominio = null, Guid? lojaPadraoId = null) =>
        new(subtitulo, null, null, null, null, null, null, dominio, null, null, lojaPadraoId);

    [Fact]
    public async Task Obter_devolve_a_vitrine_da_empresa_do_token()
    {
        var result = await _controller.Obter();

        var detalhe = DadosDe<StorefrontAdminDetalhe>(result);
        detalhe.Id.Should().Be(_vitrineA.Id);
        detalhe.EmpresaId.Should().Be(_empresaA);
    }

    [Fact]
    public async Task Obter_sem_vitrine_devolve_404()
    {
        _storefronts.GetByEmpresaAsync(_empresaA).Returns((StorefrontEntity?)null);

        var result = await _controller.Obter();

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Editar_altera_so_a_vitrine_da_empresa_do_token()
    {
        var result = await _controller.Editar(Pedido(subtitulo: "Doces da A"));

        result.Should().BeOfType<OkObjectResult>();
        _vitrineA.SubtituloPublico.Should().Be("Doces da A");
        _vitrineB.SubtituloPublico.Should().BeNull();
        await _storefronts.Received(1).UpdateAsync(_vitrineA, Arg.Any<CancellationToken>());
        await _storefronts.DidNotReceive().UpdateAsync(_vitrineB, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Editar_sem_vitrine_nao_cai_na_vitrine_de_outra_empresa()
    {
        _storefronts.GetByEmpresaAsync(_empresaA).Returns((StorefrontEntity?)null);

        var result = await _controller.Editar(Pedido(subtitulo: "invasão"));

        result.Should().BeOfType<NotFoundObjectResult>();
        _vitrineB.SubtituloPublico.Should().BeNull();
        await _storefronts.DidNotReceive().UpdateAsync(Arg.Any<StorefrontEntity>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Editar_com_loja_padrao_de_outra_empresa_devolve_400_sem_gravar()
    {
        var lojaDeB = Guid.NewGuid();
        _lojas.GetByIdAsync(_empresaA, lojaDeB).Returns((Loja?)null);

        var result = await _controller.Editar(Pedido(lojaPadraoId: lojaDeB));

        result.Should().BeOfType<BadRequestObjectResult>();
        _vitrineA.LojaPadraoId.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Editar_com_dominio_de_outra_vitrine_devolve_409_sem_gravar()
    {
        _vitrineB.DefinirDominioCustom("lojab.com.br");
        _storefronts.GetByDominioCustomAsync("lojab.com.br").Returns(_vitrineB);

        var result = await _controller.Editar(Pedido(dominio: "LojaB.com.br"));

        result.Should().BeOfType<ConflictObjectResult>();
        _vitrineA.DominioCustom.Should().BeNull();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Ativar_e_desativar_mexem_so_na_vitrine_da_empresa_do_token()
    {
        (await _controller.Ativar()).Should().BeOfType<OkObjectResult>();
        _vitrineA.Ativo.Should().BeTrue();
        _vitrineB.Ativo.Should().BeFalse();

        _vitrineB.Ativar();
        (await _controller.Desativar()).Should().BeOfType<OkObjectResult>();
        _vitrineA.Ativo.Should().BeFalse();
        _vitrineB.Ativo.Should().BeTrue();
    }

    [Fact]
    public void Sessao_sem_empresa_devolve_400_antes_da_acao()
    {
        _currentUser.EmpresaId.Returns(Guid.Empty);
        var contexto = new ActionExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), _controller);

        ((IActionFilter)_controller).OnActionExecuting(contexto);

        contexto.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Contrato_exige_policy_Admin_e_nao_aceita_id_de_vitrine_nem_de_empresa()
    {
        var tipo = typeof(StorefrontConfiguracaoController);
        tipo.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("Admin");

        var parametros = tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters()).Select(p => p.Name!.ToLowerInvariant());
        parametros.Should().NotContain(["id", "empresaid", "storefrontid"]);

        typeof(StorefrontConfiguracaoController.EditarConfiguracaoVitrineRequest).GetProperties()
            .Select(p => p.Name).Should().NotContain(["EmpresaId", "StorefrontId", "Id"]);
    }

    private static T DadosDe<T>(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return (T)ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value)!;
    }
}
