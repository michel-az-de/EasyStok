using System.Text.Json;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ImpressaoLoja;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#2199: GET/PUT da regra de impressão da loja, com ETag de versão e If-Match obrigatório.</summary>
public class LojaImpressaoControllerTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid LojaId = Guid.NewGuid();

    private const string CanonicoA4 =
        """{"v":1,"documentos":{"pedido":{"papel":"a4","template":null,"orientacao":null,"codigos":null,"copias":1}}}""";

    private readonly IRegraImpressaoLojaRepository _repo = Substitute.For<IRegraImpressaoLojaRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly LojaImpressaoController _controller;

    public LojaImpressaoControllerTests()
    {
        _currentUser.Nivel.Returns(NivelAcesso.Gerente);
        _currentUser.EmpresaId.Returns(EmpresaId);
        _controller = new LojaImpressaoController(
            new ObterRegraImpressaoLojaUseCase(_repo),
            new GravarRegraImpressaoLojaUseCase(_repo, Substitute.For<IEtiquetaTemplateRepository>(), TimeProvider.System),
            _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task Get_de_loja_que_nao_e_da_empresa_responde_404()
    {
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns((RegraImpressaoSalva?)null);

        var resposta = await _controller.Obter(LojaId, CancellationToken.None);

        resposta.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Get_devolve_a_regra_canonica_e_a_versao_na_etag()
    {
        // O jsonb devolve as chaves em outra ordem; o GET responde sempre a forma canônica.
        const string doBanco = """{"documentos":{"pedido":{"copias":1,"papel":"a4"}},"v":1}""";
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>())
            .Returns(new RegraImpressaoSalva(doBanco, 3, DateTime.UtcNow));

        var resposta = await _controller.Obter(LojaId, CancellationToken.None);

        var regra = Dados(resposta);
        regra.Versao.Should().Be(3);
        regra.Impressao!.Value.GetRawText().Should().Be(CanonicoA4);
        _controller.Response.Headers.ETag.ToString().Should().Be("\"3\"");
    }

    [Fact]
    public async Task Get_de_loja_sem_configuracao_devolve_impressao_nula_e_versao_zero()
    {
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns(new RegraImpressaoSalva(null, 0, null));

        var regra = Dados(await _controller.Obter(LojaId, CancellationToken.None));

        regra.Impressao.Should().BeNull();
        _controller.Response.Headers.ETag.ToString().Should().Be("\"0\"");
    }

    [Fact]
    public async Task Put_sem_if_match_responde_428()
    {
        var resposta = await _controller.Gravar(LojaId, PedidoA4(), CancellationToken.None);

        var objeto = resposta.Should().BeOfType<ObjectResult>().Subject;
        objeto.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
        ((ApiErrorResponse)objeto.Value!).Error.Code.Should().Be(LojaImpressaoController.CodigoSemIfMatch);
        await _repo.DidNotReceiveWithAnyArgs().GravarAsync(default, default, default, default!, default, default);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("*")]
    [InlineData("\"1\", \"2\"")]
    [InlineData("-1")]
    public async Task Put_com_if_match_ilegivel_responde_400(string ifMatch)
    {
        _controller.Request.Headers.IfMatch = ifMatch;

        var resposta = await _controller.Gravar(LojaId, PedidoA4(), CancellationToken.None);

        resposta.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("\"0\"")]
    [InlineData("W/\"0\"")]
    [InlineData("0")]
    public async Task Put_grava_e_devolve_a_versao_nova_na_etag(string ifMatch)
    {
        _controller.Request.Headers.IfMatch = ifMatch;
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns(new RegraImpressaoSalva(null, 0, null));
        _repo.GravarAsync(EmpresaId, LojaId, 0, CanonicoA4, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new GravacaoRegraImpressao(true, 1));

        var resposta = await _controller.Gravar(LojaId, PedidoA4(), CancellationToken.None);

        Dados(resposta).Versao.Should().Be(1);
        _controller.Response.Headers.ETag.ToString().Should().Be("\"1\"");
    }

    [Fact]
    public async Task Put_de_corpo_novo_sobre_versao_velha_responde_409_com_o_estado_atual()
    {
        _controller.Request.Headers.IfMatch = "\"4\"";
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>())
            .Returns(new RegraImpressaoSalva(CanonicoA4.Replace("\"a4\"", "\"a5\""), 5, DateTime.UtcNow));

        var resposta = await _controller.Gravar(LojaId, PedidoA4(), CancellationToken.None);

        var conflito = resposta.Should().BeOfType<ConflictObjectResult>().Subject;
        var erro = ((ApiErrorResponse)conflito.Value!).Error;
        erro.Code.Should().Be(LojaImpressaoController.CodigoConflito);
        JsonSerializer.Serialize(erro.Details).Should().Contain("\"Versao\":5");
        _controller.Response.Headers.ETag.ToString().Should().Be("\"5\"");
    }

    [Fact]
    public async Task Put_com_papel_fora_do_catalogo_responde_400_com_o_campo()
    {
        _controller.Request.Headers.IfMatch = "\"0\"";
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns(new RegraImpressaoSalva(null, 0, null));

        var resposta = await _controller.Gravar(LojaId, Pedido("carta"), CancellationToken.None);

        var erro = ((ApiErrorResponse)resposta.Should().BeOfType<BadRequestObjectResult>().Subject.Value!).Error;
        erro.Code.Should().Be("IMPRESSAO_INVALIDA");
        erro.Message.Should().Contain("documentos.pedido.papel");
    }

    [Fact]
    public async Task Put_em_loja_de_outra_empresa_responde_404()
    {
        _controller.Request.Headers.IfMatch = "\"0\"";
        _repo.ObterAsync(EmpresaId, LojaId, Arg.Any<CancellationToken>()).Returns((RegraImpressaoSalva?)null);

        var resposta = await _controller.Gravar(LojaId, PedidoA4(), CancellationToken.None);

        resposta.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void Put_limita_o_tamanho_do_corpo()
    {
        // A regra canônica completa tem menos de 1 KB; o limite padrão do Kestrel (30 MB) deixava amplificar o 400.
        var limite = typeof(LojaImpressaoController).GetMethod(nameof(LojaImpressaoController.Gravar))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), false).Cast<RequestSizeLimitAttribute>().SingleOrDefault();

        limite.Should().NotBeNull();
        LojaImpressaoController.TamanhoMaximoCorpo.Should().BeLessThanOrEqualTo(16 * 1024);
    }

    private static RegraImpressaoLojaResult Dados(IActionResult resposta) =>
        resposta.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ApiResponse<RegraImpressaoLojaResult>>().Subject.Data;

    private static RegraImpressaoLojaEntrada PedidoA4() => Pedido("a4");

    private static RegraImpressaoLojaEntrada Pedido(string papel) =>
        new(1, new Dictionary<string, RegraImpressaoDocumentoEntrada?> { ["pedido"] = new(papel, null, null, null, null) });
}
