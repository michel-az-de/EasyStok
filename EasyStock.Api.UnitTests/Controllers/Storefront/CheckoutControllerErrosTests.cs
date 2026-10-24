using System.Text.Json;
using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Exceptions.Storefront;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.UnitTests.Controllers.Storefront;

/// <summary>
/// estrutura-12 (banca 23/10): os dois checkouts (logado e guest) respondem os mesmos erros pela mesma tabela. Antes
/// havia ~110 linhas de catch repetidas e o "carrinho alterado" saia em ProblemDetails no logado e em envelope
/// <c>error</c> no guest.
/// </summary>
public class CheckoutControllerErrosTests
{
    private readonly CheckoutController _controller = new(null!, null!, TimeProvider.System)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private (int? Status, string Json) Mapear(Exception ex)
    {
        _controller.TentarMapearErro(ex, out var resposta).Should().BeTrue();
        var objeto = resposta.Should().BeAssignableTo<ObjectResult>().Subject;
        return (objeto.StatusCode, JsonSerializer.Serialize(objeto.Value));
    }

    [Theory]
    [MemberData(nameof(Erros))]
    public void MapeiaCadaErroDoCheckoutParaOStatusDoContrato(Exception ex, int status)
    {
        Mapear(ex).Status.Should().Be(status);
    }

    public static TheoryData<Exception, int> Erros() => new()
    {
        { new IdempotencyMismatchException(), 409 },
        { new CheckoutEmAndamentoException(), 409 },
        { new SessaoChatSiteInvalidaException(), 403 },
        { new ChatSiteIndisponivelException(), 404 },
        { new CepInvalidoException("x"), 400 },
        { new UseCaseValidationException("x"), 400 },
        { new StorefrontNaoEncontradoException("loja"), 404 },
        { new LojaFechadaException("fechada"), 409 },
        { new JanelaSemVagasException("esgotada"), 409 },
        { new CepSemCoberturaException("sem cobertura"), 422 },
        { new MercadoPagoIndisponivelException("mp fora"), 503 },
        { new TelefoneInvalidoException(), 400 },
        { new ClienteBloqueadoException(Guid.NewGuid()), 422 },
        { new RegraDeDominioVioladaException("x"), 422 },
    };

    [Fact]
    public void CarrinhoAlterado_LevaOEnvelopeErrorEOProblemDetailsNoMesmoCorpo()
    {
        var (status, json) = Mapear(new IdempotencyMismatchException());

        status.Should().Be(409);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Carrinho alterado");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CARRINHO_ALTERADO");
        doc.RootElement.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ClienteBloqueado_NaoVazaOIdNemDizQueOCadastroEstaBloqueado()
    {
        // Revisao da PR: o bloqueio vale pelo telefone; quem digita o de outra pessoa nao pode descobrir o bloqueio.
        var cliente = Guid.NewGuid();

        var (status, json) = Mapear(new ClienteBloqueadoException(cliente));

        status.Should().Be(422);
        json.Should().NotContain(cliente.ToString());
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("detail").GetString().Should().NotContainEquivalentOf("bloquead");
        doc.RootElement.GetProperty("title").GetString().Should().NotContainEquivalentOf("bloquead");
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CLIENTE_BLOQUEADO");
        doc.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegraDeDominio_UsaOMesmoTituloNosDoisCheckouts()
    {
        var (_, json) = Mapear(new RegraDeDominioVioladaException("Nome e obrigatorio."));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Dados inválidos");
    }

    [Fact]
    public void CheckoutEmAndamento_PedeParaTentarDeNovoEm2s()
    {
        var (_, json) = Mapear(new CheckoutEmAndamentoException());

        _controller.Response.Headers["Retry-After"].ToString().Should().Be("2");
        json.Should().Contain("CHECKOUT_EM_ANDAMENTO");
    }

    [Fact]
    public void ExcecaoQueNaoEDoCheckout_NaoEMapeada()
    {
        _controller.TentarMapearErro(new InvalidOperationException("boom"), out _).Should().BeFalse();
    }
}
