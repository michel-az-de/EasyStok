using System.Reflection;
using EasyStock.Api.Configuration;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #1448: a foto que a loja manda no chat do site sai para o visitante pela sessão dele, e só a da
/// conversa dele, de mensagem que ele já vê. Nota interna e mensagem de outra conversa são 404.
/// </summary>
public class ChatSiteMidiaControllerTests
{
    private const string Slug = "casa-da-baba";
    private const string Chave = "atendimento/e/c/foto.jpg";
    private readonly StorefrontEntity _loja;
    private readonly ISessaoChatSiteRepository _sessoes = Substitute.For<ISessaoChatSiteRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly ChatSiteMidiaController _controller;
    private readonly Guid _conversaId = Guid.NewGuid();

    public ChatSiteMidiaControllerTests()
    {
        _loja = StorefrontEntity.Criar(empresaId: Guid.NewGuid(), slug: Slug, tituloPublico: "Casa da Baba", pedidoMinimoEntrega: 0m);
        _loja.Ativar();
        var lojas = Substitute.For<IStorefrontRepository>();
        lojas.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(_loja);
        var flags = Substitute.For<ITenantFeatureFlagRepository>();
        flags.ListarAtivasAsync(_loja.EmpresaId, Arg.Any<CancellationToken>())
            .Returns([FeatureCatalogo.ModuloAtendimento, FeatureCatalogo.CanalChatSite]);
        var acesso = new AcessoChatSite(lojas, flags, Substitute.For<ITenantContextAccessor>(), _sessoes);
        _controller = new ChatSiteMidiaController(new ObterMidiaChatSiteUseCase(acesso, _conversas, _storage))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        _storage.ExistsAsync(Chave, Arg.Any<CancellationToken>()).Returns(true);
        _storage.DownloadAsync(Chave, Arg.Any<CancellationToken>()).Returns([9, 8, 7]);
    }

    private string SessaoComConversa(bool comConversa = true)
    {
        var token = AcessoChatSite.NovoToken();
        var sessao = SessaoChatSite.Abrir(_loja.EmpresaId, _loja.Id, AcessoChatSite.HashDoToken(token), DateTime.UtcNow);
        if (comConversa) sessao.VincularConversa(_conversaId);
        _sessoes.ObterPorTokenHashAsync(_loja.EmpresaId, sessao.TokenHash, Arg.Any<CancellationToken>()).Returns(sessao);
        return token;
    }

    private Mensagem FotoDaLoja(string? externoId = "chatsite:1")
    {
        var m = Mensagem.Saida(_loja.EmpresaId, _conversaId, AutorMensagem.Dona, DateTime.UtcNow, TipoConteudoMensagem.Imagem, "Lasanha", externoId);
        m.AnexarMidia(Chave, "image/jpeg");
        _conversas.ObterMensagemAsync(_loja.EmpresaId, _conversaId, m.Id, Arg.Any<CancellationToken>()).Returns(m);
        return m;
    }

    [Fact]
    public async Task FotoDaLoja_SaiParaOVisitanteDaSessao()
    {
        var token = SessaoComConversa();
        var foto = FotoDaLoja();

        var result = await _controller.Midia(Slug, foto.Id, token);

        var arquivo = result.Should().BeOfType<FileContentResult>().Subject;
        arquivo.ContentType.Should().Be("image/jpeg");
        arquivo.FileContents.Should().Equal(9, 8, 7);
    }

    [Fact]
    public async Task NotaInternaSemIdExterno_404()
    {
        var token = SessaoComConversa();
        var nota = FotoDaLoja(externoId: null);

        (await _controller.Midia(Slug, nota.Id, token)).Should().BeOfType<NotFoundObjectResult>();
        await _storage.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
    }

    [Fact]
    public async Task MensagemDeOutraConversa_404()
    {
        var token = SessaoComConversa();

        (await _controller.Midia(Slug, Guid.NewGuid(), token)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SessaoSemConversa_404SemConsultarMensagem()
    {
        var token = SessaoComConversa(comConversa: false);

        (await _controller.Midia(Slug, Guid.NewGuid(), token)).Should().BeOfType<NotFoundObjectResult>();
        await _conversas.DidNotReceiveWithAnyArgs().ObterMensagemAsync(default, default, default, default);
    }

    [Fact]
    public async Task ArquivoForaDoStorage_404()
    {
        var token = SessaoComConversa();
        var foto = FotoDaLoja();
        _storage.ExistsAsync(Chave, Arg.Any<CancellationToken>()).Returns(false);

        (await _controller.Midia(Slug, foto.Id, token)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task TokenInvalido_403()
    {
        var result = await _controller.Midia(Slug, Guid.NewGuid(), "inventado");

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task LojaSemChat_404()
    {
        (await _controller.Midia("outra-loja", Guid.NewGuid(), SessaoComConversa())).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void Rota_TemRateLimitDeLeitura()
    {
        var metodo = typeof(ChatSiteMidiaController).GetMethod(nameof(ChatSiteMidiaController.Midia))!;
        metodo.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be(ChatSiteRateLimit.Leitura);
        metodo.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("mensagens/{mensagemId:guid}/midia");
    }
}
