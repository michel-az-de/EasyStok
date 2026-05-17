using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Financeiro;

public class ContaReceberTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Categoria = Guid.NewGuid();

    private static ContaReceber NovaRascunho() =>
        ContaReceber.Criar(Empresa, clienteId: null, Categoria, "Cliente Y", DateTime.UtcNow);

    [Fact]
    public void Criar_inicia_em_rascunho_com_zero()
    {
        var c = NovaRascunho();
        c.Status.Should().Be(StatusContaFinanceira.Rascunho);
        c.ValorTotal.Should().Be(0m);
        c.FaturaId.Should().BeNull();
    }

    [Fact]
    public void AdicionarParcela_recalcula_valor()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 200m, DateTime.UtcNow.AddDays(15));
        c.AdicionarParcela(2, 200m, DateTime.UtcNow.AddDays(45));
        c.ValorTotal.Should().Be(400m);
    }

    [Fact]
    public void Emitir_para_aberta()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.Emitir();
        c.Status.Should().Be(StatusContaFinanceira.Aberta);
    }

    [Fact]
    public void RegistrarPagamento_marca_paga_quando_total_atingido()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.Emitir();

        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Receber, 100m, "pix", DateTime.UtcNow);
        c.Parcelas.First().RegistrarPagamento(pag);
        c.AtualizarStatusPorParcelas();

        c.Status.Should().Be(StatusContaFinanceira.Paga);
        c.TotalRecebido.Should().Be(100m);
    }

    [Fact]
    public void RegistrarPagamento_lado_pagar_em_parcela_receber_falha()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.Emitir();

        var pagPagar = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "pix", DateTime.UtcNow);
        var act = () => c.Parcelas.First().RegistrarPagamento(pagPagar);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Receber*");
    }

    [Fact]
    public void Pendente_calcula_corretamente()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.AdicionarParcela(2, 50m, DateTime.UtcNow.AddDays(60));
        c.Emitir();

        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Receber, 30m, "pix", DateTime.UtcNow);
        c.Parcelas.First().RegistrarPagamento(pag);

        c.Pendente.Should().Be(120m); // 150 - 30
    }
}
