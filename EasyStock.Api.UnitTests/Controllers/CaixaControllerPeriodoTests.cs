using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.ListarLinhasCaixa;
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
    private readonly ICaixaSaldoCalculator _calc = Substitute.For<ICaixaSaldoCalculator>();
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
        return new CaixaController(null!, null!, null!, null!, new ListarMovimentosCaixaUseCase(_repo), null!, null!, new ListarLinhasCaixaUseCase(_calc), _currentUser);
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

    // #2262: as linhas do Caixa (pedido pago pelo Mercado Pago, venda) usam fim exclusivo; o período do Extrato
    // chega inclusivo. Data pura de 05/11 consulta de 05/11 03:00Z até 06/11 03:00Z, sem perder o último instante.
    [Fact]
    public async Task ListarLinhas_DataPura_ConsultaODiaDeBrasiliaInteiroComFimExclusivo()
    {
        DateTime? ini = null, fim = null;
        _calc.GetLinhasExtrasAsync(_empresaId, Arg.Do<DateTime>(d => ini = d), Arg.Do<DateTime>(d => fim = d), null)
            .Returns((IReadOnlyList<CaixaLinhaExtraResult>)[]);
        var controller = CriarController();

        await controller.ListarLinhas(_empresaId,
            desde: new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Unspecified),
            ate: new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Unspecified));

        ini.Should().Be(new DateTime(2026, 11, 5, 3, 0, 0, DateTimeKind.Utc));
        fim.Should().Be(new DateTime(2026, 11, 6, 3, 0, 0, DateTimeKind.Utc));
    }

    // O Console manda o instante com Z e fim .999; janela de 366 dias é o máximo e ainda consulta.
    [Fact]
    public async Task ListarLinhas_InstanteComFusoNoLimiteDe366Dias_ConsultaOInstanteRecebido()
    {
        DateTime? ini = null, fim = null;
        _calc.GetLinhasExtrasAsync(_empresaId, Arg.Do<DateTime>(d => ini = d), Arg.Do<DateTime>(d => fim = d), null)
            .Returns((IReadOnlyList<CaixaLinhaExtraResult>)[]);
        var desde = new DateTime(2025, 1, 1, 3, 0, 0, DateTimeKind.Utc);
        var ate = new DateTime(2026, 1, 2, 2, 59, 59, 999, DateTimeKind.Utc);

        await CriarController().ListarLinhas(_empresaId, desde: desde, ate: ate);

        ini.Should().Be(desde);
        fim.Should().Be(ate.AddTicks(10));
    }

    // Data pura de 31/12/9999 vira DateTime.MaxValue: o fim exclusivo satura em vez de estourar em 500.
    [Fact]
    public async Task ListarLinhas_UltimoDiaRepresentavel_NaoEstoura()
    {
        DateTime? fim = null;
        _calc.GetLinhasExtrasAsync(_empresaId, Arg.Any<DateTime>(), Arg.Do<DateTime>(d => fim = d), null)
            .Returns((IReadOnlyList<CaixaLinhaExtraResult>)[]);

        await CriarController().ListarLinhas(_empresaId,
            desde: new DateTime(9999, 6, 1, 0, 0, 0, DateTimeKind.Unspecified),
            ate: new DateTime(9999, 12, 31, 0, 0, 0, DateTimeKind.Unspecified));

        fim.Should().Be(DateTime.MaxValue);
    }

    [Fact]
    public async Task ListarLinhas_SemPeriodo_Responde400SemConsultar()
    {
        var resposta = await CriarController().ListarLinhas(_empresaId, desde: DateTime.UtcNow, ate: null);

        resposta.Should().BeAssignableTo<Microsoft.AspNetCore.Mvc.ObjectResult>().Which.StatusCode.Should().Be(400);
        await _calc.DidNotReceiveWithAnyArgs().GetLinhasExtrasAsync(default, default, default, default);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ListarLinhasCaixaUseCase.DiasNoMaximo + 1)]
    public async Task ListarLinhas_PeriodoInvertidoOuLongoDemais_Recusa(int dias)
    {
        var desde = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var acao = () => CriarController().ListarLinhas(_empresaId, desde: desde, ate: desde.AddDays(dias));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await _calc.DidNotReceiveWithAnyArgs().GetLinhasExtrasAsync(default, default, default, default);
    }
}
