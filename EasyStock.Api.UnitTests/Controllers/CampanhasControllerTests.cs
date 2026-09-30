using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>S28: códigos HTTP do cadastro de campanhas (regra violada 400, outra empresa 404).</summary>
public class CampanhasControllerTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ICampanhaRepository _repo = Substitute.For<ICampanhaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly CampanhasController _controller;

    public CampanhasControllerTests()
    {
        // Nível padrão do substituto seria SuperAdmin (0), que exige empresaId na query.
        _currentUser.Nivel.Returns(EasyStock.Domain.Enums.NivelAcesso.Gerente);
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());

        var relogio = TimeProvider.System;
        _controller = new CampanhasController(
            new ListarCampanhasUseCase(_repo),
            new ObterCampanhaUseCase(_repo),
            new CriarCampanhaUseCase(_repo, _uow, relogio),
            new AtualizarCampanhaUseCase(_repo, _uow, relogio),
            new CancelarCampanhaUseCase(_repo, _uow),
            _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static CampanhaBody Body(DateTime? disparoEm, string mensagem = "Oi {{nome}}") =>
        new("Bolo de fubá", mensagem, null, null, new FiltroCampanhaBody(true, null, null, null, null), ["vegano"],
            disparoEm, null, false, null);

    [Fact]
    public async Task CriarAgendadaDevolve201()
    {
        var resultado = await _controller.Criar(Body(DateTime.UtcNow.AddDays(1)), null, CancellationToken.None);

        var criado = resultado.Should().BeOfType<CreatedResult>().Subject;
        criado.Location.Should().StartWith("/api/campanhas/");
        await _repo.Received(1).AddAsync(
            Arg.Is<Campanha>(c => c.EmpresaId == _empresaId && c.Status == StatusCampanha.Agendada), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisparoNoPassadoDevolve400()
    {
        (await _controller.Criar(Body(DateTime.UtcNow.AddMinutes(-5)), null, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CampanhaDeOutraEmpresaDevolve404()
    {
        (await _controller.Obter(Guid.NewGuid(), null, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.Atualizar(Guid.NewGuid(), Body(null), null, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
        (await _controller.Cancelar(Guid.NewGuid(), null, CancellationToken.None)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CancelarDevolveCampanhaCancelada()
    {
        var campanha = Campanha.Criar(_empresaId, Guid.NewGuid(),
            new DadosCampanha("x", "y", null, null, FiltroCampanha.ParaTodos, [], null, false, null), DateTime.UtcNow);
        _repo.ObterAsync(_empresaId, campanha.Id, Arg.Any<CancellationToken>()).Returns(campanha);

        (await _controller.Cancelar(campanha.Id, null, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        campanha.Status.Should().Be(StatusCampanha.Cancelada);

        (await _controller.Cancelar(campanha.Id, null, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>("cancelar duas vezes é regra violada");
    }
}
