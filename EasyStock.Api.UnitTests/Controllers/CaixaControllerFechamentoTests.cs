using System.Security.Claims;
using EasyStock.Api.Controllers;
using EasyStock.Api.UnitTests.Helpers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
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
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>().SetupExecuteInTransactionSemRetry<FechamentoCaixaResult>();
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
        return new CaixaController(null!, fechar, null!, null!, null!, null!, null!, null!, _currentUser)
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

    private CaixaController CriarControllerDeEstorno(MovimentoCaixa movimento, params Claim[] claims)
    {
        _currentUser.Nivel.Returns(NivelAcesso.Gerente);
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(_usuarioId);
        _repo.GetMovimentoAsync(_empresaId, movimento.Id).Returns(movimento);

        var estornar = new EstornarMovimentoCaixaUseCase(_repo, _uow, null!, Substitute.For<ILogger<EstornarMovimentoCaixaUseCase>>());
        return new CaixaController(null!, null!, null!, estornar, null!, null!, null!, null!, _currentUser)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Teste")) },
            },
        };
    }

    // #2583: o estorno gravava em EstornadoPorNome o nome que viesse no corpo.
    [Fact]
    public async Task Estornar_GravaONomeDoToken_EIgnoraODoCorpo()
    {
        var saida = MovimentoCaixa.Criar(_empresaId, "saida", 20m, DateTime.UtcNow.AddMinutes(-5));
        var controller = CriarControllerDeEstorno(saida, new Claim("nome", "Dona Maria"));

        await controller.EstornarMovimento(saida.Id,
            new EstornarMovimentoCaixaCommand(_empresaId, saida.Id, Motivo: "lancado errado", UsuarioNome: "Outra Pessoa"));

        saida.EstornadoEm.Should().NotBeNull();
        saida.EstornadoPorNome.Should().Be("Dona Maria");
    }

    [Fact]
    public async Task Estornar_SemNomeNoToken_NaoAceitaONomeDoCorpo()
    {
        var saida = MovimentoCaixa.Criar(_empresaId, "saida", 20m, DateTime.UtcNow.AddMinutes(-5));
        var controller = CriarControllerDeEstorno(saida);

        await controller.EstornarMovimento(saida.Id,
            new EstornarMovimentoCaixaCommand(_empresaId, saida.Id, UsuarioNome: "Outra Pessoa"));

        saida.EstornadoPorNome.Should().BeNull();
    }
}
