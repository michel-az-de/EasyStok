using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #2084 (A3, decisão do Felipe): a abertura automática pendente só é confirmada por gerente ou dona. A operadora
/// continua abrindo o caixa normalmente, mas no ramo de confirmação recebe 403 com mensagem clara.
/// </summary>
public class CaixaControllerConfirmarAberturaTests
{
    private readonly ICaixaRepository _repo = Substitute.For<ICaixaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _empresaId = Guid.NewGuid();

    private CaixaController Controller(NivelAcesso nivel, bool comAberturaPendente)
    {
        _currentUser.Nivel.Returns(nivel);
        _currentUser.EmpresaId.Returns(_empresaId);
        _repo.GetFechamentoDoDiaAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>()).Returns((FechamentoCaixa?)null);
        _repo.GetAberturaPendenteAsync(Arg.Any<Guid>(), Arg.Any<Guid?>()).Returns((MovimentoCaixa?)null);
        var movimentos = new List<MovimentoCaixa>();
        if (comAberturaPendente)
        {
            var auto = MovimentoCaixa.Criar(_empresaId, "abertura", 130m);
            auto.ConfirmacaoPendente = true;
            movimentos.Add(auto);
        }
        _repo.GetMovimentosDoDiaAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>()).Returns(movimentos);
        var abrir = new AbrirCaixaUseCase(_repo, _uow, Substitute.For<ILogger<AbrirCaixaUseCase>>());
        return new CaixaController(abrir, null!, null!, null!, null!, null!, null!, _currentUser);
    }

    [Fact]
    public async Task Operadora_nao_confirma_abertura_automatica()
    {
        var act = () => Controller(NivelAcesso.Operador, comAberturaPendente: true)
            .Abrir(new AbrirCaixaCommand(_empresaId, 130m));

        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*gerente*");
        await _repo.DidNotReceive().UpdateMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Operadora_que_forja_a_permissao_no_corpo_tambem_nao_confirma()
    {
        var act = () => Controller(NivelAcesso.Operador, comAberturaPendente: true)
            .Abrir(new AbrirCaixaCommand(_empresaId, 130m, PodeConfirmarAberturaAutomatica: true));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(NivelAcesso.Gerente)]
    [InlineData(NivelAcesso.Admin)]
    public async Task Gerente_ou_dona_confirma_abertura_automatica(NivelAcesso nivel)
    {
        await Controller(nivel, comAberturaPendente: true).Abrir(new AbrirCaixaCommand(_empresaId, 130m));

        await _repo.Received(1).UpdateMovimentoAsync(Arg.Is<MovimentoCaixa>(m => !m.ConfirmacaoPendente));
        await _repo.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Operadora_abre_o_caixa_normalmente_sem_abertura_pendente()
    {
        await Controller(NivelAcesso.Operador, comAberturaPendente: false).Abrir(new AbrirCaixaCommand(_empresaId, 80m));

        await _repo.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.Tipo == "abertura" && m.Valor == 80m));
    }
}
