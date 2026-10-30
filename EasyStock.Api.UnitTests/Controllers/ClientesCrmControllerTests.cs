using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Cliente.Foto;
using EasyStock.Application.UseCases.ClienteCrm;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>S24: códigos HTTP do CRM leve (tag duplicada 409, nota com pedido de outro cliente 400).</summary>
public class ClientesCrmControllerTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Cliente _cliente;
    private readonly ClientesCrmController _controller;

    public ClientesCrmControllerTests()
    {
        // Nível padrão do substituto seria SuperAdmin (0), que exige empresaId na query.
        _currentUser.Nivel.Returns(EasyStock.Domain.Enums.NivelAcesso.Gerente);
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());
        _cliente = Cliente.Criar(_empresaId, "Maria");
        _clientes.GetByIdAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _clientes.GetByIdWithDetailsAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _crm.ObterComTagsAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(_cliente);

        var relogio = TimeProvider.System;
        _controller = new ClientesCrmController(
            new ListarTagsClienteUseCase(_crm),
            new AdicionarTagClienteUseCase(_crm, _uow, relogio),
            new RemoverTagClienteUseCase(_crm, _uow),
            new ListarNotasClienteUseCase(_clientes, _crm),
            new AdicionarNotaClienteUseCase(_clientes, _crm, _uow, relogio),
            new DefinirBloqueioClienteUseCase(_clientes, _uow, relogio),
            new DefinirPreferenciasClienteUseCase(_clientes, _uow, relogio),
            new ObterDossieClienteUseCase(
                _clientes, _crm, Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(),
                Substitute.For<IConversaRepository>()),
            _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task TagDuplicadaDevolve409()
    {
        (await _controller.AdicionarTag(_cliente.Id, new AdicionarTagBody("Vegano"), null, CancellationToken.None))
            .Should().BeOfType<CreatedResult>();

        (await _controller.AdicionarTag(_cliente.Id, new AdicionarTagBody("vegano"), null, CancellationToken.None))
            .Should().BeOfType<ConflictObjectResult>();

        _cliente.Tags.Should().ContainSingle();
    }

    [Fact]
    public async Task TagInvalidaDevolve400()
    {
        (await _controller.AdicionarTag(_cliente.Id, new AdicionarTagBody("!!!"), null, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();
        await _crm.Received(1).ObterComTagsAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotaComPedidoDeOutroClienteDevolve400()
    {
        var pedidoDeOutro = Guid.NewGuid();
        _crm.PedidoEhDoClienteAsync(_empresaId, pedidoDeOutro, _cliente.Id, Arg.Any<CancellationToken>()).Returns(false);

        var resultado = await _controller.AdicionarNota(
            _cliente.Id, new AdicionarNotaBody("chegou frio", pedidoDeOutro), null, CancellationToken.None);

        resultado.Should().BeOfType<BadRequestObjectResult>();
        await _crm.Received(1).PedidoEhDoClienteAsync(_empresaId, pedidoDeOutro, _cliente.Id, Arg.Any<CancellationToken>());
        await _crm.DidNotReceiveWithAnyArgs().AdicionarNotaAsync(default!, default);
    }

    [Fact]
    public async Task NotaGravaONomeDoUsuarioENaoOId()
    {
        // #1474 B1: o JWT usa NameClaimType = "sub"; Identity.Name devolve o id, não o nome.
        var usuarioId = Guid.NewGuid();
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", usuarioId.ToString()), new Claim("nome", "Thati"), new Claim("email", "thati@casa.com")],
            "Bearer", "sub", "nivel"));

        var resultado = await _controller.AdicionarNota(
            _cliente.Id, new AdicionarNotaBody("prefere sem lactose"), null, CancellationToken.None);

        resultado.Should().BeOfType<CreatedResult>();
        await _crm.Received(1).AdicionarNotaAsync(Arg.Is<ClienteNota>(n => n.Autor == "Thati"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClienteDeOutraEmpresaDevolve404()
    {
        (await _controller.Bloquear(Guid.NewGuid(), new BloquearBody("golpe"), null, CancellationToken.None))
            .Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task BloquearDevolveEstado()
    {
        var resultado = await _controller.Bloquear(_cliente.Id, new BloquearBody("golpe"), null, CancellationToken.None);

        resultado.Should().BeOfType<OkObjectResult>();
        _cliente.Bloqueado.Should().BeTrue();
        _cliente.MotivoBloqueio.Should().Be("golpe");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FichaPdfDevolveArquivoPrivadoDoClienteDaSessao(bool empresaExplicita)
    {
        var renderer = Substitute.For<IFichaClientePdfRenderer>();
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nfixture do controller");
        renderer.RenderAsync(Arg.Any<DossieClienteDto>(), Arg.Any<byte[]?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(bytes);
        var fotos = new ClienteFotoUseCase(_clientes, Substitute.For<IFileStorage>(), Substitute.For<IImageProcessor>(), _uow);
        var resultado = await _controller.FichaPdf(_cliente.Id, empresaExplicita ? _empresaId : null, renderer, fotos, CancellationToken.None);
        var arquivo = resultado.Should().BeOfType<FileContentResult>().Subject;
        arquivo.ContentType.Should().Be("application/pdf");
        arquivo.FileDownloadName.Should().Be($"ficha-cliente-{_cliente.Id:D}.pdf");
        arquivo.FileContents.Should().Equal(bytes);
        _controller.Response.Headers.CacheControl.ToString().Should().Be("private, no-store");
        await renderer.Received(1).RenderAsync(Arg.Is<DossieClienteDto>(d => d.Cliente.Id == _cliente.Id),
            null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(NivelAcesso.Admin)]
    [InlineData(NivelAcesso.Gerente)]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.SuperAdmin)]
    public async Task FichaPdfEmpresaDivergenteDevolve400SemConsultarDadosOuFoto(NivelAcesso nivel)
    {
        _currentUser.Nivel.Returns(nivel);
        var renderer = Substitute.For<IFichaClientePdfRenderer>();
        var storage = Substitute.For<IFileStorage>();
        var fotos = new ClienteFotoUseCase(_clientes, storage, Substitute.For<IImageProcessor>(), _uow);
        _clientes.ClearReceivedCalls();
        _crm.ClearReceivedCalls();

        (await _controller.FichaPdf(_cliente.Id, Guid.NewGuid(), renderer, fotos, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();

        _clientes.ReceivedCalls().Should().BeEmpty();
        _crm.ReceivedCalls().Should().BeEmpty();
        renderer.ReceivedCalls().Should().BeEmpty();
        storage.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(NivelAcesso.Admin, false)]
    [InlineData(NivelAcesso.Admin, true)]
    [InlineData(NivelAcesso.Gerente, false)]
    [InlineData(NivelAcesso.Gerente, true)]
    [InlineData(NivelAcesso.Operador, false)]
    [InlineData(NivelAcesso.Operador, true)]
    [InlineData(NivelAcesso.SuperAdmin, false)]
    [InlineData(NivelAcesso.SuperAdmin, true)]
    public async Task FichaPdfJwtSemEmpresaDevolve400MesmoComQuerySemAcessarDadosOuFoto(NivelAcesso nivel, bool empresaExplicita)
    {
        _currentUser.Nivel.Returns(nivel);
        _currentUser.EmpresaId.Returns(Guid.Empty);
        var renderer = Substitute.For<IFichaClientePdfRenderer>();
        var storage = Substitute.For<IFileStorage>();
        var fotos = new ClienteFotoUseCase(_clientes, storage, Substitute.For<IImageProcessor>(), _uow);
        _clientes.ClearReceivedCalls();
        _crm.ClearReceivedCalls();

        (await _controller.FichaPdf(_cliente.Id, empresaExplicita ? _empresaId : null, renderer, fotos, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();

        _clientes.ReceivedCalls().Should().BeEmpty();
        _crm.ReceivedCalls().Should().BeEmpty();
        renderer.ReceivedCalls().Should().BeEmpty();
        storage.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task FichaPdfDeOutroTenantDevolve404SemAcessarFotoOuRenderer()
    {
        var outro = Cliente.Criar(Guid.NewGuid(), "Cliente de outra empresa");
        _clientes.GetByIdWithDetailsAsync(outro.EmpresaId, outro.Id).Returns(outro);
        var renderer = Substitute.For<IFichaClientePdfRenderer>();
        var storage = Substitute.For<IFileStorage>();
        var fotos = new ClienteFotoUseCase(_clientes, storage, Substitute.For<IImageProcessor>(), _uow);
        (await _controller.FichaPdf(outro.Id, null, renderer, fotos, CancellationToken.None))
            .Should().BeOfType<NotFoundObjectResult>();
        await _clientes.Received(1).GetByIdWithDetailsAsync(_empresaId, outro.Id);
        await renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default, default, default);
        await storage.DidNotReceiveWithAnyArgs().DownloadAsync(default!, default);
    }

    [Fact]
    public async Task DossieDoClienteDevolveProjecao()
    {
        var resultado = await _controller.Dossie(_cliente.Id, null, CancellationToken.None);

        var ok = resultado.Should().BeOfType<OkObjectResult>().Subject;
        var dossie = (DossieClienteDto)ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value)!;
        dossie.Cliente.Id.Should().Be(_cliente.Id);
        dossie.Cliente.Nome.Should().Be("Maria");
    }

    [Fact]
    public async Task DossieDeClienteDeOutraEmpresaDevolve404() =>
        (await _controller.Dossie(Guid.NewGuid(), null, CancellationToken.None))
            .Should().BeOfType<NotFoundObjectResult>();
}
