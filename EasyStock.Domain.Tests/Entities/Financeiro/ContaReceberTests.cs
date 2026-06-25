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

    [Fact]
    public void Cancelar_zera_valorTotal_e_pendente()
    {
        // #687/BUG-009: conta cancelada NAO pode exibir Pendente. Antes, Cancelar() nao
        // recalculava ValorTotal e a conta cancelada seguia mostrando "Pendente R$ 555".
        var c = NovaRascunho();
        c.AdicionarParcela(1, 555m, DateTime.UtcNow.AddDays(30));
        c.Emitir();
        c.Pendente.Should().Be(555m);

        c.Cancelar("erro de lancamento", userId: null);

        c.Status.Should().Be(StatusContaFinanceira.Cancelada);
        c.ValorTotal.Should().Be(0m);
        c.Pendente.Should().Be(0m);
    }

    [Fact]
    public void Pendente_zero_quando_cancelada_mesmo_com_valorTotal_legado()
    {
        // Defesa-em-profundidade (#687): registro legado cancelado cujo ValorTotal foi
        // persistido antes do fix do Cancelar(). O getter defensivo zera o Pendente.
        var c = NovaRascunho();
        c.AdicionarParcela(1, 555m, DateTime.UtcNow.AddDays(30));
        c.Emitir();
        c.Status = StatusContaFinanceira.Cancelada; // simula legado: status sem recalcular

        c.ValorTotal.Should().Be(555m);  // ValorTotal "sujo" preservado
        c.Pendente.Should().Be(0m);      // getter respeita o status Cancelada
    }
}
