using System.Security.Claims;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#2305 (CAIXA-5): o autor do movimento vem do nome da sessão, também para operador.</summary>
public class CaixaControllerAutorTests
{
    [Fact]
    public async Task RegistrarMovimento_GravaONomeDaSessaoComoAutor()
    {
        var empresaId = Guid.NewGuid();
        var repo = Substitute.For<ICaixaRepository>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.Nivel.Returns(NivelAcesso.Operador);
        currentUser.EmpresaId.Returns(empresaId);
        repo.GetFechamentoDoDiaAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>()).Returns((FechamentoCaixa?)null);
        var registrar = new RegistrarMovimentoCaixaUseCase(repo, Substitute.For<IUnitOfWork>(),
            new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(repo)),
            Substitute.For<ILogger<RegistrarMovimentoCaixaUseCase>>());
        var controller = new CaixaController(null!, null!, registrar, null!, null!, null!, null!, null!, currentUser)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("nome", "Ana")], "test"))
                }
            }
        };

        await controller.RegistrarMovimento(new RegistrarMovimentoCaixaCommand(empresaId, "entrada", 10m, RegistradoPorNome: "Falso"));

        await repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.RegistradoPorNome == "Ana"));
    }
}
