using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Financeiro;

public class ContaPagarTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Categoria = Guid.NewGuid();

    private static ContaPagar NovaRascunho() =>
        ContaPagar.Criar(Empresa, fornecedorId: null, Categoria, "Fornecedor X", DateTime.UtcNow);

    [Fact]
    public void Criar_inicia_em_rascunho_com_valor_zero()
    {
        var c = NovaRascunho();
        c.Status.Should().Be(StatusContaFinanceira.Rascunho);
        c.ValorTotal.Should().Be(0m);
        c.Parcelas.Should().BeEmpty();
    }

    [Fact]
    public void Criar_rejeita_descricao_vazia()
    {
        var act = () => ContaPagar.Criar(Empresa, null, Categoria, " ", DateTime.UtcNow);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Criar_rejeita_categoria_vazia()
    {
        var act = () => ContaPagar.Criar(Empresa, null, Guid.Empty, "Desc", DateTime.UtcNow);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void AdicionarParcela_recalcula_valor_total()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.AdicionarParcela(2, 50m, DateTime.UtcNow.AddDays(60));
        c.ValorTotal.Should().Be(150m);
        c.Parcelas.Should().HaveCount(2);
    }

    [Fact]
    public void AdicionarParcela_rejeita_numero_duplicado()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        var act = () => c.AdicionarParcela(1, 50m, DateTime.UtcNow.AddDays(60));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void AdicionarParcela_rejeita_em_conta_emitida()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.Emitir();
        var act = () => c.AdicionarParcela(2, 50m, DateTime.UtcNow.AddDays(60));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Emitir_rejeita_sem_parcelas()
    {
        var c = NovaRascunho();
        var act = () => c.Emitir();
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Emitir_idempotente_em_aberta()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.Emitir();
        c.Emitir();
        c.Status.Should().Be(StatusContaFinanceira.Aberta);
    }

    [Fact]
    public void Cancelar_rejeita_em_paga()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.Emitir();
        // Simular conta paga
        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        c.Parcelas.First().RegistrarPagamento(pag);
        c.AtualizarStatusPorParcelas();
        c.Status.Should().Be(StatusContaFinanceira.Paga);

        var act = () => c.Cancelar("Mudei de ideia", null);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void Cancelar_propaga_para_parcelas_pendentes()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.AdicionarParcela(2, 100m, DateTime.UtcNow.AddDays(60));
        c.Emitir();

        c.Cancelar("Errei", null);

        c.Status.Should().Be(StatusContaFinanceira.Cancelada);
        c.Parcelas.Should().AllSatisfy(p => p.Status.Should().Be(StatusParcela.Cancelada));
    }

    [Fact]
    public void Cancelar_zera_valorTotal_e_pendente()
    {
        // #687/BUG-009: conta cancelada nao pode exibir Pendente (espelho do ContaReceber).
        var c = NovaRascunho();
        c.AdicionarParcela(1, 555m, DateTime.UtcNow.AddDays(30));
        c.Emitir();
        c.Pendente.Should().Be(555m);

        c.Cancelar("erro de lancamento", null);

        c.Status.Should().Be(StatusContaFinanceira.Cancelada);
        c.ValorTotal.Should().Be(0m);
        c.Pendente.Should().Be(0m);
    }

    [Fact]
    public void MarcarVencidaSeAplicavel_marca_quando_ha_parcela_vencida()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(-5));
        c.Emitir();
        c.MarcarVencidaSeAplicavel(DateTime.UtcNow);
        c.Status.Should().Be(StatusContaFinanceira.Vencida);
    }

    [Fact]
    public void StatusEfetivo_deriva_Vencida_sem_mutar_o_Status_armazenado()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(-5));
        c.Emitir(); // Status armazenado = Aberta

        // Deriva Vencida por data (BUG-022) MAS nao persiste — quem persiste e o job diario.
        c.StatusEfetivo(DateTime.UtcNow).Should().Be(StatusContaFinanceira.Vencida);
        c.Status.Should().Be(StatusContaFinanceira.Aberta);
    }

    [Fact]
    public void StatusEfetivo_mantem_Aberta_quando_nada_vencido()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(15));
        c.Emitir();
        c.StatusEfetivo(DateTime.UtcNow).Should().Be(StatusContaFinanceira.Aberta);
    }

    [Fact]
    public void StatusEfetivo_nao_mostra_Rascunho_como_Vencida()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(-5)); // vencida, mas conta e rascunho
        c.StatusEfetivo(DateTime.UtcNow).Should().Be(StatusContaFinanceira.Rascunho);
    }

    [Fact]
    public void AtualizarStatusPorParcelas_define_parcial_quando_uma_paga_e_outras_abertas()
    {
        var c = NovaRascunho();
        c.AdicionarParcela(1, 100m, DateTime.UtcNow.AddDays(30));
        c.AdicionarParcela(2, 100m, DateTime.UtcNow.AddDays(60));
        c.Emitir();

        var pag = PagamentoParcela.CriarConfirmado(Empresa, TipoLadoFinanceiro.Pagar, 100m, "dinheiro", DateTime.UtcNow);
        c.Parcelas.First().RegistrarPagamento(pag);
        c.AtualizarStatusPorParcelas();
        c.Status.Should().Be(StatusContaFinanceira.ParcialmentePaga);
    }
}
