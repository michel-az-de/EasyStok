using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ListarMovimentosCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// PEDIDO-01 (banca 23/10): <c>desde</c>/<c>ate</c> do extrato do caixa chegavam ao repositorio com Kind
/// Unspecified/Local (o Npgsql recusa contra timestamptz: 500) e <c>ate</c> de data pura escondia o ultimo dia.
/// </summary>
public class CaixaControllerPeriodoTests
{
    private readonly ICaixaRepository _repo = Substitute.For<ICaixaRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private DateTime? _desdeRecebido;
    private DateTime? _ateRecebido;

    private CaixaController CriarController()
    {
        _currentUser.Nivel.Returns(NivelAcesso.Operador);
        _currentUser.EmpresaId.Returns(_empresaId);
        _repo.ListMovimentosAsync(_empresaId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Do<DateTime?>(d => _desdeRecebido = d), Arg.Do<DateTime?>(d => _ateRecebido = d),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(((IEnumerable<MovimentoCaixa>)[], 0));
        return new CaixaController(null!, null!, null!, null!, new ListarMovimentosCaixaUseCase(_repo), null!, null!, _currentUser);
    }

    [Fact]
    public async Task ListarMovimentos_DataPura_ViraDiaDeBrasiliaEmUtc_ComUltimoDiaIncluso()
    {
        var controller = CriarController();

        await controller.ListarMovimentos(_empresaId,
            desde: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified),
            ate: new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Unspecified));

        _desdeRecebido.Should().Be(new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc));
        _desdeRecebido!.Value.Kind.Should().Be(DateTimeKind.Utc);
        _ateRecebido.Should().Be(new DateTime(2026, 10, 24, 3, 0, 0, DateTimeKind.Utc).AddTicks(-10));
        _ateRecebido!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task ListarMovimentos_InstanteComKindLocal_ChegaComoUtcDoMesmoInstante()
    {
        // financeiroApi.js do console manda ISO com "Z": o binder entrega Kind=Local.
        var instante = new DateTime(2026, 10, 23, 15, 30, 0, DateTimeKind.Utc);
        var controller = CriarController();

        await controller.ListarMovimentos(_empresaId, desde: instante.ToLocalTime(), ate: instante.AddHours(1).ToLocalTime());

        _desdeRecebido.Should().Be(instante);
        _desdeRecebido!.Value.Kind.Should().Be(DateTimeKind.Utc);
        _ateRecebido.Should().Be(instante.AddHours(1));
        _ateRecebido!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }
}
