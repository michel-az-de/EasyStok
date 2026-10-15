using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.ValueObjects;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N12: o coletor de rotinas agendadas como o Worker o monta (<c>AddEasyStockPostgreInfrastructure</c> e
/// <c>AddEasyStockApplication</c>), sob o papel <c>rls_test_client</c> (NOBYPASSRLS). A "última execução" é o próprio
/// evento do dia e o índice único <c>(EmpresaId, CorrelationId)</c> trava dois hosts.
/// </summary>
public class ColetorRotinasAgendadasIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    /// <summary>01/10/2026 23:30Z é 20:30 em Brasília.</summary>
    private static readonly DateTimeOffset Noite = new(2026, 10, 1, 23, 30, 0, TimeSpan.Zero);

    private sealed class RelogioFalso(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = inicio;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private ServiceProvider Host(TimeProvider relogio) =>
        _s.ConstruirProviderDoWorker(papelRls: true, ajustar: services => services.AddSingleton(relogio));

    private static async Task RodarAsync(ServiceProvider host)
    {
        using var escopo = host.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<INotificacoesColetorOrchestrator>().ExecutarRodadaAsync();
    }

    private async Task<Guid> SemearRotinaAsync(
        Guid? empresaId, string horario = "20:00", bool ativa = true, string? parametros = null)
    {
        var rotina = RotinaNotificacao.Criar($"resumo-{Guid.NewGuid():N}", "Resumo diário", TipoEventoNotificacao.ResumoDiario,
            TriggerTipoRotina.Evento, "resumo_diario_email_v1", CategoriaConteudoNotificacao.Operacional, empresaId: empresaId);
        rotina.DefinirParametros(parametros ?? $$$"""{"modoCanais":"todos","audiencia":"admins","agenda":{"horario":"{{{horario}}}"}}""", "teste");
        if (ativa) rotina.Ativar("teste");
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifRotinas.Add(rotina);
        await db.SaveChangesAsync();
        return rotina.Id;
    }

    private async Task<List<EventoNotificacao>> ResumosDaEmpresaAsync(Guid empresaId) =>
        (await _s.LerEventosDaEmpresaAsync(empresaId)).Where(e => e.Tipo == TipoEventoNotificacao.ResumoDiario).ToList();

    [SkippableFact]
    public async Task TresRodadasNoMesmoDiaGeramUmEvento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var rotinaId = await SemearRotinaAsync(empresa);
        await using var host = Host(new RelogioFalso(Noite));

        await RodarAsync(host);
        await RodarAsync(host);
        await RodarAsync(host);

        var eventos = await ResumosDaEmpresaAsync(empresa);
        eventos.Should().ContainSingle();
        eventos[0].CorrelationId.Should().Be($"agenda:{rotinaId:N}:20261001");
        eventos[0].EmpresaId.Should().Be(empresa);
        using var payload = JsonDocument.Parse(eventos[0].PayloadJson);
        payload.RootElement.GetProperty("data").GetString().Should().Be("01/10/2026", "a data é a do dia local, não a do dia UTC");
    }

    [SkippableFact]
    public async Task AntesDoHorarioNaoGeraEvento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        await SemearRotinaAsync(empresa, horario: "21:00");
        await using var host = Host(new RelogioFalso(Noite)); // 20:30 em Brasília

        await RodarAsync(host);

        (await ResumosDaEmpresaAsync(empresa)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task DoisHostsSimultaneosGeramUmEvento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        // Várias empresas para a corrida acontecer de verdade: com uma só, um host pode terminar antes de o outro começar.
        var empresas = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            var empresa = await _s.SemearEmpresaAsync();
            await SemearRotinaAsync(empresa);
            empresas.Add(empresa);
        }
        var relogio = new RelogioFalso(Noite);
        await using var hostA = Host(relogio);
        await using var hostB = Host(relogio);

        await Task.WhenAll(RodarAsync(hostA), RodarAsync(hostB));

        foreach (var empresa in empresas)
            (await ResumosDaEmpresaAsync(empresa)).Should().ContainSingle("o índice único (EmpresaId, CorrelationId) trava o segundo host");
    }

    [SkippableFact]
    public async Task NovoDiaGeraNovoEvento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var rotinaId = await SemearRotinaAsync(empresa);
        var relogio = new RelogioFalso(Noite);
        await using var host = Host(relogio);

        await RodarAsync(host);
        relogio.Agora = Noite.AddDays(1);
        await RodarAsync(host);

        (await ResumosDaEmpresaAsync(empresa)).Select(e => e.CorrelationId).Should().BeEquivalentTo(
            [$"agenda:{rotinaId:N}:20261001", $"agenda:{rotinaId:N}:20261002"]);
    }

    [SkippableFact]
    public async Task DiaPerdidoNaoGeraResumoDeOntem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        await SemearRotinaAsync(empresa);
        // 00:10 de Brasília do dia 02 (03:10Z): o Worker voltou depois da meia-noite e o dia 01 se perdeu.
        await using var host = Host(new RelogioFalso(new DateTimeOffset(2026, 10, 2, 3, 10, 0, TimeSpan.Zero)));

        await RodarAsync(host);

        (await ResumosDaEmpresaAsync(empresa)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task VoltarAs2350GeraNoMesmoDia()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        await SemearRotinaAsync(empresa);
        // 23:50 de Brasília do dia 01 (02:50Z do dia 02).
        await using var host = Host(new RelogioFalso(new DateTimeOffset(2026, 10, 2, 2, 50, 0, TimeSpan.Zero)));

        await RodarAsync(host);

        (await ResumosDaEmpresaAsync(empresa)).Should().ContainSingle().Which.CorrelationId.Should().EndWith(":20261001");
    }

    [SkippableFact]
    public async Task RotinaGlobalInativaNaoRoda()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        await SemearRotinaAsync(null, ativa: false);
        await using var host = Host(new RelogioFalso(Noite));

        await RodarAsync(host);

        (await ResumosDaEmpresaAsync(empresa)).Should().BeEmpty("a rotina global é molde: só a empresa com rotina própria ativa recebe");
    }

    [SkippableFact]
    public async Task RotinaGlobalAtivaComAgendaEhIgnorada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        await SemearRotinaAsync(null, ativa: true);
        await using var host = Host(new RelogioFalso(Noite));

        await RodarAsync(host);

        (await ResumosDaEmpresaAsync(empresa)).Should().BeEmpty("agenda em rotina global não vira evento por empresa");
    }

    [SkippableFact]
    public async Task RotinaDeOutraEmpresaNaoVaza()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = await _s.SemearEmpresaAsync();
        var empresaB = await _s.SemearEmpresaAsync();
        var empresaC = await _s.SemearEmpresaAsync();
        await SemearRotinaAsync(empresaA);
        await SemearRotinaAsync(empresaC, ativa: false);
        await using var host = Host(new RelogioFalso(Noite));

        await RodarAsync(host);

        (await ResumosDaEmpresaAsync(empresaA)).Should().ContainSingle();
        (await ResumosDaEmpresaAsync(empresaB)).Should().BeEmpty("a empresa B não tem rotina");
        (await ResumosDaEmpresaAsync(empresaC)).Should().BeEmpty("a rotina da C está inativa");

        // E o papel de produção, com o tenant da B ligado, não enxerga o resumo da A.
        using var escopoDaB = host.CreateScope();
        escopoDaB.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresaB);
        var db = escopoDaB.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.NotifEventos.IgnoreQueryFilters().CountAsync(e => e.Tipo == TipoEventoNotificacao.ResumoDiario))
            .Should().Be(0, "sob RLS com o tenant da B nenhum resumo da A aparece");
    }

    [SkippableFact]
    public async Task ResumoVemDaEmpresaCerta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = await _s.SemearEmpresaAsync();
        var empresaB = await _s.SemearEmpresaAsync();
        await SemearEntreguesAsync(empresaA, 100m, 100m);
        await SemearEntreguesAsync(empresaB, 50m);
        // 00:00 está sempre devido; o relógio é o real porque o resumo do dia lê a janela de Brasília de agora.
        await SemearRotinaAsync(empresaA, horario: "00:00");
        await SemearRotinaAsync(empresaB, horario: "00:00");
        await using var host = Host(TimeProvider.System);

        await RodarAsync(host);

        var a = JsonDocument.Parse((await ResumosDaEmpresaAsync(empresaA)).Single().PayloadJson).RootElement;
        var b = JsonDocument.Parse((await ResumosDaEmpresaAsync(empresaB)).Single().PayloadJson).RootElement;
        a.GetProperty("entregues").GetInt32().Should().Be(2);
        a.GetProperty("faturamento").GetString().Should().Be("R$ 200,00");
        b.GetProperty("entregues").GetInt32().Should().Be(1);
        b.GetProperty("faturamento").GetString().Should().Be("R$ 50,00");
    }

    private async Task SemearEntreguesAsync(Guid empresaId, params decimal[] totais)
    {
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        foreach (var total in totais)
        {
            var pedido = Pedido.Criar(empresaId);
            pedido.Status = "entregue";
            pedido.EntreguEm = DateTime.UtcNow;
            pedido.Total = Dinheiro.FromDecimal(total);
            db.Pedidos.Add(pedido);
        }
        await db.SaveChangesAsync();
    }
}
