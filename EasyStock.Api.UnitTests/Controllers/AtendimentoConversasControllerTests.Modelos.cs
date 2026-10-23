using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums;
using EasyStock.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public partial class AtendimentoConversasControllerTests
{
    private EnviarModeloConversaUseCase EnvioModelo(ICatalogoModelosAtendimento catalogo) => new(
        _repositorio, Substitute.For<IClienteRepository>(), catalogo,
        new PoliticaEnvioCliente(Substitute.For<IConsentimentoContatoRepository>()),
        new ResolvedorCanal([_canal]), new FakeUnitOfWork(), TimeProvider.System);

    [Fact]
    public async Task Modelos_PermissaoAntesDaConsultaOuDoEnvio()
    {
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(false);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        (await _controller.ListarModelos(new(catalogo), cache)).Should().BeOfType<ForbidResult>();
        (await _controller.EnviarModelo(Guid.NewGuid(), new(Guid.NewGuid(), "retomar", "pt_BR", []), EnvioModelo(catalogo)))
            .Should().BeOfType<ForbidResult>();
        catalogo.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Modelos_CacheCurtoPorEmpresa_AtualizacaoExplicita()
    {
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        catalogo.ListarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var listar = new ListarModelosAtendimentoUseCase(catalogo);
        await _controller.ListarModelos(listar, cache);
        await _controller.ListarModelos(listar, cache);
        await catalogo.Received(1).ListarAsync(_empresaId, Arg.Any<CancellationToken>());
        await _controller.ListarModelos(listar, cache, atualizar: true);
        await catalogo.Received(2).ListarAsync(_empresaId, Arg.Any<CancellationToken>());
        var outra = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(outra);
        await _controller.ListarModelos(listar, cache);
        await catalogo.Received(1).ListarAsync(outra, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Modelos_FalhaNoCatalogo502_Inexistente404_InputInvalido400()
    {
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        catalogo.ListarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<ModeloAtendimento>>>(_ => throw new CatalogoModelosException("Falha"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        (await _controller.ListarModelos(new(catalogo), cache)).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(502);
        (await _controller.EnviarModelo(Guid.NewGuid(), new(Guid.NewGuid(), "modelo", "pt_BR", []), EnvioModelo(catalogo)))
            .Should().BeOfType<NotFoundObjectResult>();
        (await _controller.EnviarModelo(Guid.NewGuid(), new(Guid.Empty, "modelo", "pt_BR", []), EnvioModelo(catalogo)))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    private IniciarConversaClienteUseCase Iniciar(ICatalogoModelosAtendimento catalogo, IClienteRepository clientes) =>
        new(_repositorio, clientes, catalogo, EnvioModelo(catalogo), new FakeUnitOfWork(), TimeProvider.System);

    /// <summary>#1408: chamar no WhatsApp exige a mesma permissão de atender e responde o 409 que o console já mostra.</summary>
    [Fact]
    public async Task IniciarConversa_PermissaoDeAtenderE409SemConexao()
    {
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        catalogo.ListarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<ModeloAtendimento>>>(
            _ => throw CatalogoModelosException.SemConexao("Conecte o WhatsApp da empresa em Integrações."));
        var clientes = Substitute.For<IClienteRepository>();
        var cliente = new EasyStock.Domain.Entities.Cliente { Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Ana", Telefone = "11988887777" };
        clientes.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        var body = new IniciarConversaClienteBody(cliente.Id, Guid.NewGuid(), "boas_vindas", "pt_BR", ["Ana"]);

        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(false);
        (await _controller.Iniciar(body, Iniciar(catalogo, clientes))).Should().BeOfType<ForbidResult>();
        await catalogo.DidNotReceiveWithAnyArgs().ListarAsync(default, default);

        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(true);
        var resposta = (await _controller.Iniciar(body, Iniciar(catalogo, clientes))).Should().BeAssignableTo<ObjectResult>().Subject;
        resposta.StatusCode.Should().Be(409);
        resposta.Value.Should().BeOfType<ApiErrorResponse>().Subject.Error.Code.Should().Be("WHATSAPP_NAO_CONECTADO");
    }

    // #1765-9: sem a conexão do WhatsApp não há o que a Meta responder. É 409 com a frase para a dona, não 502.
    [Fact]
    public async Task Modelos_SemConexaoDoWhatsApp409ComFraseClara()
    {
        var catalogo = Substitute.For<ICatalogoModelosAtendimento>();
        catalogo.ListarAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<ModeloAtendimento>>>(
            _ => throw CatalogoModelosException.SemConexao("Conecte o WhatsApp da empresa em Integrações."));
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var resposta = (await _controller.ListarModelos(new(catalogo), cache)).Should().BeAssignableTo<ObjectResult>().Subject;

        resposta.StatusCode.Should().Be(409);
        var erro = resposta.Value.Should().BeOfType<ApiErrorResponse>().Subject.Error;
        erro.Code.Should().Be("WHATSAPP_NAO_CONECTADO");
        erro.Message.Should().Contain("Conecte o WhatsApp");
    }
}
