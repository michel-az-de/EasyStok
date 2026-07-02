using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Trava a origem server-side no CaixaController (#766): origem="mobile" enviada no
/// corpo NAO pode burlar as guardas FIN-003 (metodo/justificativa + anti-caixa-negativo).
/// So o caminho mobile legitimo (MobileCashController) seta origem="mobile" server-side.
/// </summary>
public class CaixaControllerOrigemTests
{
    private readonly ICaixaRepository _repo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _empresaId = Guid.NewGuid();

    private CaixaController CriarController()
    {
        _currentUser.Nivel.Returns(NivelAcesso.Operador);
        _currentUser.EmpresaId.Returns(_empresaId);
        _repo.GetFechamentoDoDiaAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>())
            .Returns((FechamentoCaixa?)null);

        var registrar = new RegistrarMovimentoCaixaUseCase(
            _repo, _uow,
            new ObterCaixaDiaUseCase(new CaixaSaldoCalculator(_repo)),
            Substitute.For<ILogger<RegistrarMovimentoCaixaUseCase>>());

        // Apenas RegistrarMovimento e exercitado; os demais UCs nao sao tocados neste teste.
        return new CaixaController(null!, null!, registrar, null!, null!, null!, null!, _currentUser);
    }

    [Fact]
    public async Task RegistrarMovimento_Saida_ComOrigemMobileNoBody_AindaExigeGuardasFIN003()
    {
        var controller = CriarController();

        // Saida sem metodo/justificativa, tentando burlar via origem="mobile" no corpo.
        var command = new RegistrarMovimentoCaixaCommand(
            _empresaId, "saida", 100m, Descricao: null, Metodo: null, Origem: "mobile");

        var act = () => controller.RegistrarMovimento(command);

        // Com a origem forcada para "web", a guarda FIN-003 dispara (exige metodo).
        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*método*");
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task RegistrarMovimento_Persiste_Origem_Web_IgnorandoBody()
    {
        MovimentoCaixa? persistido = null;
        await _repo.AddMovimentoAsync(Arg.Do<MovimentoCaixa>(m => persistido = m));

        var controller = CriarController();
        // Entrada (nao passa por FIN-003), origem="mobile" no corpo deve ser ignorada.
        var command = new RegistrarMovimentoCaixaCommand(_empresaId, "entrada", 50m, Origem: "mobile");

        await controller.RegistrarMovimento(command);

        persistido.Should().NotBeNull();
        persistido!.Origem.Should().Be("web");
    }
}
