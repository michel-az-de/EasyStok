using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Operacao.Atraso;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.IntegrationTests.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Operacao;

/// <summary>
/// N11 sob o papel de produção (<c>rls_test_client</c>, NOBYPASSRLS): o aviso de prazo grava o evento no escopo do tenant
/// do fato, na mesma transação da marca (pedido atrasado) e uma única vez (impressão travada), sem 42501.
/// </summary>
public class PrazoEstouradoPedidoAtrasoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private async Task<Pedido> SemearPedidoAsync(Guid empresaId, DateTime? inicioPrevisto)
    {
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = StatusPedidoMapper.Aguardando;
        pedido.DefinirInicioPrevisto(inicioPrevisto);
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }

    [SkippableFact]
    public async Task MarcaEEventoSaemNoMesmoCommitSobRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var pedido = await SemearPedidoAsync(empresa, DateTime.UtcNow.AddMinutes(-20));
        await using var provider = _s.ConstruirProviderDeJobDaApi(papelRls: true);

        IReadOnlyList<PedidoAtrasoCandidato> candidatos;
        await using (var escopo = provider.CreateAsyncScope())
            candidatos = await escopo.ServiceProvider.GetRequiredService<IPedidoStorefrontRepository>()
                .ListarAtrasoNaoNotificadoAsync(DateTime.UtcNow, 100);
        foreach (var candidato in candidatos.Where(c => c.EmpresaId == empresa))
        {
            // Um escopo por pedido, como o PedidoAtrasoJob.
            await using var escopo = provider.CreateAsyncScope();
            (await escopo.ServiceProvider.GetRequiredService<NotificarAtrasoPedidoUseCase>().ExecuteAsync(candidato))
                .Should().BeTrue();
        }

        var eventos = await _s.LerEventosDaEmpresaAsync(empresa);
        var evento = eventos.Should().ContainSingle(e => e.Tipo == TipoEventoNotificacao.PrazoEstourado).Subject;
        evento.CorrelationId.Should().Be($"prazo:pedido_atrasado:{pedido.Id:N}");
        evento.PayloadJson.Should().Contain($"prazo:pedido_atrasado:{pedido.Id}");
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        (await db.Pedidos.AsNoTracking().IgnoreQueryFilters().SingleAsync(p => p.Id == pedido.Id))
            .AtrasoNotificadoEm.Should().NotBeNull("a marca saiu no mesmo commit do evento");
    }

    [SkippableFact]
    public async Task EventoDeImpressaoGravaSobRlsComTenantDaImpressao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var pedido = await SemearPedidoAsync(empresa, null);
        var impressao = ImpressaoPendente.CriarCanhoto(empresa, null, pedido.Id, DateTime.UtcNow.AddMinutes(-10));
        await using (var seed = fixture.CreateDbContext())
        {
            using var _ = seed.UseRowLevelSecurityBypass();
            seed.ImpressoesPendentes.Add(impressao);
            await seed.SaveChangesAsync();
        }

        await using var provider = _s.ConstruirProviderDeJobDaApi(papelRls: true);
        for (var rodada = 0; rodada < 3; rodada++)
        {
            IReadOnlyList<ImpressaoAtrasada> atrasadas;
            await using (var escopo = provider.CreateAsyncScope())
                atrasadas = await escopo.ServiceProvider.GetRequiredService<AlertarImpressoesAtrasadasUseCase>().ExecuteAsync();
            foreach (var atrasada in atrasadas.Where(a => a.EmpresaId == empresa))
            {
                await using var escopo = provider.CreateAsyncScope();
                await escopo.ServiceProvider.GetRequiredService<NotificarImpressaoTravadaUseCase>().ExecuteAsync(atrasada);
            }
        }

        var eventos = await _s.LerEventosDaEmpresaAsync(empresa);
        eventos.Should().ContainSingle(e => e.Tipo == TipoEventoNotificacao.PrazoEstourado,
            "o SSE repete a cada rodada, o e-mail não")
            .Which.CorrelationId.Should().Be($"prazo:impressao_travada:{impressao.Id:N}");
    }
}
