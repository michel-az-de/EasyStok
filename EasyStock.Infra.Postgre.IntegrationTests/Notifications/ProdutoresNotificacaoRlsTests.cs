using EasyStock.Api.BackgroundServices;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Notifications.Agendamento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N1, os produtores de evento sob o papel de produção (<c>rls_test_client</c>, NOBYPASSRLS): publicam no escopo do
/// tenant, enxergam o catálogo global e só carimbam o dedup depois de o evento ter sido gravado.
/// </summary>
public class ProdutoresNotificacaoRlsTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    [SkippableFact]
    public async Task Lembrete_de_pedido_agendado_grava_o_evento_sob_RLS_sem_42501()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var pedido = await SemearPedidoAgendadoAsync(empresa, DateTime.UtcNow.AddMinutes(30));
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true);
        var tick = new LembretesPedidoAgendadoTick(provider, NullLogger<LembretesPedidoAgendadoTick>.Instance);

        await tick.ExecutarAsync(CancellationToken.None);

        // A 30 min da entrega valem o lembrete "no dia" e o de "1 hora"; o de 10 min ainda não.
        var eventos = await _s.LerEventosDaEmpresaAsync(empresa);
        eventos.Select(e => e.Tipo).Should().BeEquivalentTo(
            [TipoEventoNotificacao.PedidoAgendadoHoje, TipoEventoNotificacao.PedidoAgendadoEm1Hora],
            "o evento é gravado no escopo do tenant do pedido, sem 42501");
        await using var db = fixture.CreateDbContext();
        var gravado = await db.Set<Order>().AsNoTracking().IgnoreQueryFilters().SingleAsync(o => o.Id == pedido);
        gravado.AgendamentoNotificadoDiaEm.Should().NotBeNull("o carimbo vem depois do commit do evento");
        gravado.AgendamentoNotificado1hEm.Should().NotBeNull();
        gravado.AgendamentoNotificado10minEm.Should().BeNull();
    }

    private async Task<string> SemearPedidoAgendadoAsync(Guid empresaId, DateTime entregaEm)
    {
        var pedido = new Order
        {
            Id = $"ord-{Guid.NewGuid():N}", ClientSnapshotName = "Maria", Status = "aguardando",
            EmpresaId = empresaId, ScheduledDeliveryAt = entregaEm
        };
        await using var db = fixture.CreateDbContext();
        db.Set<Order>().Add(pedido);
        await db.SaveChangesAsync();
        return pedido.Id;
    }

    [SkippableFact]
    public async Task Caixa_esquecido_enfileira_antes_de_carimbar_o_dedup()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var usuario = await _s.SemearUsuarioAsync(empresa);
        await _s.SemearCatalogoGlobalAsync(TipoEventoNotificacao.CaixaAbertoEsquecido, CanalNotificacao.InApp);
        var abertura = MovimentoCaixa.Criar(empresa, "abertura", 100m, DateTime.UtcNow.AddDays(-2));
        abertura.RegistradoPorUserId = usuario;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.MovimentosCaixa.Add(abertura);
            await seed.SaveChangesAsync();
        }
        await using var provider = _s.ConstruirProviderDeJobDaApi(papelRls: true);
        var job = new CaixaEsquecidoJob(provider, NullLogger<CaixaEsquecidoJob>.Instance);

        await job.ProcessarComLockAsync(CancellationToken.None);

        var mensagens = await _s.LerMensagensDaEmpresaAsync(empresa);
        mensagens.Should().ContainSingle(m => m.UsuarioDestinoId == usuario,
            "a rotina global é visível ao publicar: sem ela o evento fecha Processado sem outbox");
        await using var db = fixture.CreateDbContext();
        (await db.MovimentosCaixa.AsNoTracking().IgnoreQueryFilters().SingleAsync(m => m.Id == abertura.Id)).NotificadoEsquecidoEm
            .Should().NotBeNull("o dedup é carimbado depois de o aviso ter entrado no outbox");
    }

    [SkippableFact]
    public async Task Contas_vencendo_nao_carimba_o_dedup_quando_a_publicacao_falha()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var parcela = await SemearParcelaPagarVencendoHojeAsync(empresa);
        var notificador = Substitute.For<INotificadorService>();
        notificador.PublicarEventoAsync(default, default, default, default!, default, default)
            .ThrowsForAnyArgs(new InvalidOperationException("falha simulada na publicação"));
        await using var provider = _s.ConstruirProviderDeJobDaApi(papelRls: true,
            ajustar: services => services.AddScoped(_ => notificador));
        // N12: o aviso de contas vem desligado; este teste prova o carimbo com ele ligado.
        var opcoes = Options.Create(new EasyStock.Api.Configuration.BackgroundJobOptions { EnableContaFinanceiraNotificacoes = true });
        var job = new ContaFinanceiraVencimentoJob(provider, NullLogger<ContaFinanceiraVencimentoJob>.Instance, opcoes);

        await job.ProcessarAsync(CancellationToken.None);

        await using var db = fixture.CreateDbContext();
        var gravada = await db.ParcelasPagar.AsNoTracking().IgnoreQueryFilters().SingleAsync(p => p.Id == parcela);
        gravada.NotificadaD1Em.Should().BeNull("a publicação falhou: sem carimbo, a próxima rodada tenta de novo");
    }

    private async Task<Guid> SemearParcelaPagarVencendoHojeAsync(Guid empresaId)
    {
        var hoje = DateTime.UtcNow.Date;
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        var categoria = CategoriaFinanceira.Criar(empresaId, "Despesa de teste", TipoCategoriaFinanceira.Despesa);
        db.CategoriasFinanceiras.Add(categoria);
        var conta = ContaPagar.Criar(empresaId, null, categoria.Id, "Conta que vence hoje", hoje);
        conta.AdicionarParcela(1, 100m, hoje.AddHours(10));
        conta.Emitir();
        db.ContasPagar.Add(conta);
        await db.SaveChangesAsync();
        return conta.Parcelas.Single().Id;
    }
}
