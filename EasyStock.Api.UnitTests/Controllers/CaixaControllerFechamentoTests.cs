using System.Security.Claims;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #2082 B13 (#1776-13): o fechamento nunca gravava <c>FechadoPorNome</c>; "Últimos fechamentos" e o "Fechado às 20:14"
/// ficavam sem nome. O nome vem do usuário autenticado, nunca do corpo (o corpo não pode atribuir o fechamento a outro).
/// </summary>
public class CaixaControllerFechamentoTests
{
    private readonly ICaixaRepository _repo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private FechamentoCaixa? _gravado;
    private MovimentoCaixa? _marcador;

    private CaixaController CriarController(params Claim[] claims)
    {
        _currentUser.Nivel.Returns(NivelAcesso.Gerente);
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(_usuarioId);
        _repo.GetAberturaPendenteAsync(_empresaId, Arg.Any<Guid?>())
            .Returns(MovimentoCaixa.Criar(_empresaId, "abertura", 100m, DateTime.UtcNow.AddMinutes(-30)));
        _repo.GetMovimentosDoDiaAsync(_empresaId, Arg.Any<DateOnly>(), Arg.Any<Guid?>()).Returns(new List<MovimentoCaixa>());
        _repo.AddFechamentoAsync(Arg.Do<FechamentoCaixa>(f => _gravado = f)).Returns(Task.CompletedTask);
        _repo.AddMovimentoAsync(Arg.Do<MovimentoCaixa>(m => _marcador = m)).Returns(Task.CompletedTask);

        var fechar = new FecharCaixaUseCase(_repo, _uow, Substitute.For<ILogger<FecharCaixaUseCase>>());
        return new CaixaController(null!, fechar, null!, null!, null!, null!, null!, _currentUser)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Teste")) },
            },
        };
    }

    [Fact]
    public async Task Fechar_GravaONomeDoUsuarioAutenticado_NoFechamentoENoMarcador()
    {
        var controller = CriarController(new Claim("nome", "Dona Maria"));

        await controller.Fechar(new FecharCaixaCommand(_empresaId));

        _gravado.Should().NotBeNull();
        _gravado!.FechadoPorNome.Should().Be("Dona Maria");
        _gravado.FechadoPorUserId.Should().Be(_usuarioId);
        _marcador!.RegistradoPorNome.Should().Be("Dona Maria");
    }

    [Fact]
    public async Task Fechar_IgnoraONomeDoCorpo()
    {
        var controller = CriarController(new Claim("nome", "Dona Maria"));

        await controller.Fechar(new FecharCaixaCommand(_empresaId, FechadoPorNome: "Outra Pessoa"));

        _gravado!.FechadoPorNome.Should().Be("Dona Maria");
    }

    [Fact]
    public async Task Fechar_SemNomeNoToken_NaoInventaNemAceitaONomeDoCorpo()
    {
        var controller = CriarController();

        await controller.Fechar(new FecharCaixaCommand(_empresaId, FechadoPorNome: "Outra Pessoa"));

        _gravado!.FechadoPorNome.Should().BeNull();
    }

    [Fact]
    public async Task Fechar_NomeLongoDemais_CabeNaColuna()
    {
        var controller = CriarController(new Claim("nome", new string('x', 300)));

        await controller.Fechar(new FecharCaixaCommand(_empresaId));

        _gravado!.FechadoPorNome.Should().HaveLength(120);
    }
}
