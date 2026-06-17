using EasyStock.Application.Operacao;

namespace EasyStock.Application.Tests.Operacao;

public class FleetHealthScoringTests
{
    private static readonly DateTime Now = new(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);

    private static FleetHealthSignals Saudavel() => new(
        Suspensa: false,
        VendasHojeCount: 5,
        UltimaVendaEm: Now,            // vendeu hoje
        TicketsAbertos: 0,
        TicketsSlaViolado: 0,
        FaturasVencidasCount: 0,
        TrialFim: null);

    [Fact]
    public void Cliente_saudavel_fica_ok_sem_motivos()
    {
        var r = FleetHealthScoring.Avaliar(Saudavel(), Now);

        r.Band.Should().Be(FleetHealthScoring.BandOk);
        r.Motivos.Should().BeEmpty();
    }

    [Fact]
    public void Fatura_vencida_eh_critico()
    {
        var r = FleetHealthScoring.Avaliar(Saudavel() with { FaturasVencidasCount = 1 }, Now);

        r.Band.Should().Be(FleetHealthScoring.BandCrit);
        r.Motivos.Should().Contain(FleetHealthScoring.MotivoFaturaVencida);
    }

    [Fact]
    public void Assinatura_suspensa_eh_critico()
    {
        var r = FleetHealthScoring.Avaliar(Saudavel() with { Suspensa = true }, Now);

        r.Band.Should().Be(FleetHealthScoring.BandCrit);
        r.Motivos.Should().Contain(FleetHealthScoring.MotivoSuspensa);
    }

    [Fact]
    public void Sla_violado_eh_critico_e_nao_duplica_com_ticket_aberto()
    {
        var r = FleetHealthScoring.Avaliar(Saudavel() with { TicketsAbertos = 2, TicketsSlaViolado = 1 }, Now);

        r.Band.Should().Be(FleetHealthScoring.BandCrit);
        r.Motivos.Should().Contain(FleetHealthScoring.MotivoSlaViolado);
        r.Motivos.Should().NotContain(FleetHealthScoring.MotivoTicketAberto);
    }

    [Fact]
    public void Periodo_de_teste_acabando_eh_atencao()
    {
        var r = FleetHealthScoring.Avaliar(Saudavel() with { TrialFim = Now.AddDays(2) }, Now);

        r.Band.Should().Be(FleetHealthScoring.BandWarn);
        r.Motivos.Should().Contain(FleetHealthScoring.MotivoTrialVencendo);
    }

    [Fact]
    public void Sem_vendas_ha_mais_de_uma_semana_eh_atencao()
    {
        var r = FleetHealthScoring.Avaliar(Saudavel() with { UltimaVendaEm = Now.AddDays(-10), VendasHojeCount = 0 }, Now);

        r.Band.Should().Be(FleetHealthScoring.BandWarn);
        r.Motivos.Should().Contain(FleetHealthScoring.MotivoSemVendas);
    }

    [Fact]
    public void Nunca_vendeu_conta_como_sem_vendas()
    {
        var r = FleetHealthScoring.Avaliar(Saudavel() with { UltimaVendaEm = null, VendasHojeCount = 0 }, Now);

        r.Motivos.Should().Contain(FleetHealthScoring.MotivoSemVendas);
    }

    [Fact]
    public void Suspensa_nao_recebe_motivo_de_sem_vendas()
    {
        // suspensa já é crítico; não polui o card com "sem vendas".
        var r = FleetHealthScoring.Avaliar(Saudavel() with { Suspensa = true, UltimaVendaEm = null, VendasHojeCount = 0 }, Now);

        r.Motivos.Should().NotContain(FleetHealthScoring.MotivoSemVendas);
    }

    [Fact]
    public void Critico_ordena_acima_de_atencao()
    {
        var crit = FleetHealthScoring.Avaliar(Saudavel() with { FaturasVencidasCount = 1 }, Now);
        var warn = FleetHealthScoring.Avaliar(Saudavel() with { TrialFim = Now.AddDays(1) }, Now);

        crit.Severidade.Should().BeGreaterThan(warn.Severidade);
    }
}
