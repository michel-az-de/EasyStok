using EasyStock.Application.Operacao;

namespace EasyStock.Application.Tests.Operacao;

public class FleetHealthScoringTests
{
    // Instante de referencia fixo (sem relogio ambiente).
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private static FleetHealthSignals Saudavel() => new(
        VendasCount: 12,
        PedidosTravados: 0,
        DevicesAtivos: 2,
        DevicesTotal: 2,
        TicketsSlaViolado: 0,
        FaturaVencida: false,
        TrialFim: null);

    [Fact]
    public void Loja_saudavel_tem_score_cheio_banda_ok_sem_flags()
    {
        var r = FleetHealthScoring.Compute(Saudavel(), Now);

        r.Score.Should().Be(100);
        r.Band.Should().Be(FleetHealthScoring.BandOk);
        r.Flags.Should().BeEmpty();
    }

    [Fact]
    public void Sem_vendas_e_devices_offline_cai_para_crit_com_as_flags()
    {
        var s = Saudavel() with { VendasCount = 0, DevicesAtivos = 0 };

        var r = FleetHealthScoring.Compute(s, Now);

        // 100 - 35 (offline) - 15 (sem vendas) = 50 -> warn; soma SLA p/ crit no proximo teste.
        r.Score.Should().Be(50);
        r.Band.Should().Be(FleetHealthScoring.BandWarn);
        r.Flags.Should().Contain(new[] { FleetHealthScoring.FlagDevicesOffline, FleetHealthScoring.FlagSemVendas });
    }

    [Fact]
    public void Multiplos_problemas_levam_a_banda_crit()
    {
        var s = Saudavel() with
        {
            VendasCount = 0,
            DevicesAtivos = 0,
            TicketsSlaViolado = 2,
            FaturaVencida = true,
        };

        var r = FleetHealthScoring.Compute(s, Now);

        // 100 -35 -15 -15 (sla) -15 (fatura) = 20 -> crit.
        r.Score.Should().Be(20);
        r.Band.Should().Be(FleetHealthScoring.BandCrit);
        r.Flags.Should().Contain(FleetHealthScoring.FlagSlaViolado);
        r.Flags.Should().Contain(FleetHealthScoring.FlagFaturaVencida);
    }

    [Fact]
    public void Em_risco_significa_score_abaixo_de_70()
    {
        // Apenas devices offline: 100 - 35 = 65 (< 70) => em risco (warn).
        var r = FleetHealthScoring.Compute(Saudavel() with { DevicesAtivos = 0 }, Now);

        r.Score.Should().Be(65);
        (r.Score < FleetHealthScoring.LimiarRisco).Should().BeTrue();
        r.Band.Should().Be(FleetHealthScoring.BandWarn);
    }

    [Fact]
    public void Pedidos_travados_tem_penalidade_escalonada_com_teto()
    {
        var um = FleetHealthScoring.Compute(Saudavel() with { PedidosTravados = 1 }, Now);
        var muitos = FleetHealthScoring.Compute(Saudavel() with { PedidosTravados = 20 }, Now);

        um.Score.Should().Be(90);   // 100 - (5 + 1*5)
        muitos.Score.Should().Be(80); // teto: 100 - 20
        um.Flags.Should().Contain(FleetHealthScoring.FlagPedidosTravados);
    }

    [Fact]
    public void Trial_vencendo_em_ate_3_dias_adiciona_flag()
    {
        var venceLogo = Saudavel() with { TrialFim = Now.AddDays(2) };
        var venceLonge = Saudavel() with { TrialFim = Now.AddDays(10) };

        var rLogo = FleetHealthScoring.Compute(venceLogo, Now);
        var rLonge = FleetHealthScoring.Compute(venceLonge, Now);

        rLogo.Flags.Should().Contain(FleetHealthScoring.FlagTrialVencendo);
        rLogo.Score.Should().Be(90);
        rLonge.Flags.Should().NotContain(FleetHealthScoring.FlagTrialVencendo);
        rLonge.Score.Should().Be(100);
    }

    [Fact]
    public void Score_nunca_passa_de_0_a_100()
    {
        var pessimo = new FleetHealthSignals(
            VendasCount: 0,
            PedidosTravados: 50,
            DevicesAtivos: 0,
            DevicesTotal: 5,
            TicketsSlaViolado: 9,
            FaturaVencida: true,
            TrialFim: Now.AddDays(1));

        var r = FleetHealthScoring.Compute(pessimo, Now);

        r.Score.Should().BeInRange(0, 100);
        r.Band.Should().Be(FleetHealthScoring.BandCrit);
    }
}
