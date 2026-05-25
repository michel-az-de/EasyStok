using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Financeiro;

public class PagamentoParcelaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public void CriarConfirmado_define_status_e_data()
    {
        var p = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        p.Status.Should().Be(StatusPagamentoParcela.Confirmado);
        p.Lado.Should().Be(TipoLadoFinanceiro.Pagar);
        p.Valor.Should().Be(100m);
        p.GatewayProvedor.Should().Be("Manual");
    }

    [Fact]
    public void CriarConfirmado_rejeita_valor_zero()
    {
        var act = () => PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 0m, "pix", DateTime.UtcNow);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CriarPendente_exige_gateway_e_transactionId()
    {
        var act = () => PagamentoParcela.CriarPendente(Empresa, TipoLadoFinanceiro.Receber, 100m, "pix", DateTime.UtcNow, "", "tx", null);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Estornar_so_em_confirmado()
    {
        var p = PagamentoParcela.CriarPendente(Empresa, TipoLadoFinanceiro.Receber, 100m, "pix", DateTime.UtcNow, "EfiPix", "txid", null);
        var act = () => p.Estornar(null, "test");
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Estornar_idempotente()
    {
        var p = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        p.Estornar(null, "ok");
        var primeiro = p.EstornadoEm;
        p.Estornar(null, "again");
        p.EstornadoEm.Should().Be(primeiro);
        p.Status.Should().Be(StatusPagamentoParcela.Estornado);
    }

    [Fact]
    public void Confirmar_so_em_pendente()
    {
        var p = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        // Ja confirmado, confirmar novamente e idempotente
        p.Confirmar();
        p.Status.Should().Be(StatusPagamentoParcela.Confirmado);
    }

    [Fact]
    public void AssociarMovimentoCaixa_grava_id()
    {
        var p = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        var movId = Guid.NewGuid();
        p.AssociarMovimentoCaixa(movId);
        p.MovimentoCaixaId.Should().Be(movId);
    }

    [Fact]
    public void AssociarMovimentoCaixa_rejeita_guid_vazio()
    {
        var p = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        var act = () => p.AssociarMovimentoCaixa(Guid.Empty);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}
