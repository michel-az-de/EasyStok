using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Atendimento.Ocorrencias;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Ocorrencias;

/// <summary>F14 (#1244), lacuna 3: o reembolso da ocorrência (S27, RN-36) sai do caixa do dia.</summary>
public class LancarReembolsoNoCaixaUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _pedidoId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly ICaixaRepository _caixa = Substitute.For<ICaixaRepository>();
    private readonly ICobrancaPedidoRepository _cobrancas = Substitute.For<ICobrancaPedidoRepository>();

    private LancarReembolsoNoCaixaUseCase Lancar() => new(_caixa, _cobrancas);

    private Ocorrencia Ocorrencia() => Domain.Entities.Atendimento.Ocorrencia.Abrir(
        _empresaId, _pedidoId, Guid.NewGuid(), null, OrigemOcorrencia.Dona,
        CategoriaOcorrencia.ProdutoImproprio, "bolo chegou azedo", Agora.AddMinutes(-5));

    private void CobrancaPagaNoCredito()
    {
        var c = CobrancaPedido.CriarOnline(_empresaId, _pedidoId, 80m, "pref-1", "https://mp/link",
            Agora.AddHours(1), 1, Agora.AddMinutes(-30));
        c.MarcarPaga("pay-1", 80m, "credito", Agora.AddMinutes(-20));
        _cobrancas.ListarDoPedidoAsync(_empresaId, _pedidoId, Arg.Any<CancellationToken>()).Returns(new[] { c });
    }

    [Fact]
    public async Task ReembolsoEfetuado_LancaSaidaComOrigemOcorrenciaEMetodoDaCobranca()
    {
        CobrancaPagaNoCredito();
        var ocorrencia = Ocorrencia();
        MovimentoCaixa? lancado = null;
        await _caixa.AddMovimentoAsync(Arg.Do<MovimentoCaixa>(m => lancado = m));

        await Lancar().ExecuteAsync(new LancarReembolsoNoCaixaInput(
            ocorrencia, new ReembolsoResultado(SituacaoReembolso.Efetuado, "reembolso_efetuado", 30m, "ref-1"), _usuarioId, Agora));

        lancado.Should().NotBeNull();
        lancado!.Tipo.Should().Be("saida");
        lancado.Valor.Should().Be(30m);
        lancado.Metodo.Should().Be("credito");
        lancado.Categoria.Should().Be("Reembolso");
        lancado.Origem.Should().Be(LancarReembolsoNoCaixaUseCase.Origem);
        lancado.Referencia.Should().Be(ocorrencia.Id.ToString());
        lancado.DataMovimento.Should().Be(Agora);
        lancado.RegistradoPorUserId.Should().Be(_usuarioId);
        lancado.EmpresaId.Should().Be(_empresaId);
    }

    [Fact]
    public async Task ReembolsoManual_LancaSaidaNoMetodoOutro()
    {
        _cobrancas.ListarDoPedidoAsync(_empresaId, _pedidoId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CobrancaPedido>());
        MovimentoCaixa? lancado = null;
        await _caixa.AddMovimentoAsync(Arg.Do<MovimentoCaixa>(m => lancado = m));

        await Lancar().ExecuteAsync(new LancarReembolsoNoCaixaInput(
            Ocorrencia(), new ReembolsoResultado(SituacaoReembolso.ManualNecessario, "reembolso_manual_necessario", 25m, null), _usuarioId, Agora));

        lancado!.Valor.Should().Be(25m);
        lancado.Metodo.Should().Be("outro");
    }

    [Fact]
    public async Task ReembolsoQueFalhou_NaoMexeNoCaixa()
    {
        await Lancar().ExecuteAsync(new LancarReembolsoNoCaixaInput(
            Ocorrencia(), new ReembolsoResultado(SituacaoReembolso.Falhou, "estorno_recusado", 30m, null), _usuarioId, Agora));

        await _caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task MesmaOcorrenciaDuasVezes_LancaUmaSo()
    {
        CobrancaPagaNoCredito();
        var ocorrencia = Ocorrencia();
        _caixa.ExisteMovimentoAsync(_empresaId, LancarReembolsoNoCaixaUseCase.Origem, ocorrencia.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns(true);

        await Lancar().ExecuteAsync(new LancarReembolsoNoCaixaInput(
            ocorrencia, new ReembolsoResultado(SituacaoReembolso.Efetuado, "reembolso_efetuado", 30m, "ref-1"), _usuarioId, Agora));

        await _caixa.DidNotReceive().AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }
}
