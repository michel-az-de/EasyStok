using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications.Plataforma;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N6 em Postgres real, sob o papel de produção (<c>rls_test_client</c>, NOBYPASSRLS, ADR-0010). O webhook de plataforma
/// é anônimo e não tem tenant: sem fixar o tenant da empresa do opaco, a RLS zera a leitura do outbox. As sementes e as
/// conferências vão pelo superusuário.
/// </summary>
public class StatusWhatsAppPlataformaTenantTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string NumeroPlataforma = "7770009999";

    private readonly MotorNotificacoesSuporte _s = new(fixture);

    /// <summary>Provider como a API do webhook: sem usuário, filtro do EF com tenant vazio e RLS valendo.</summary>
    private ServiceProvider Webhook() => _s.ConstruirProviderDeJobDaApi(papelRls: true, ajustar: services =>
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Notifications:WhatsApp:Plataforma:PhoneNumberId"] = NumeroPlataforma }).Build()));

    private async Task<OutboxMensagemNotificacao> SemearIndeterminadaAsync()
    {
        var semente = await _s.SemearAsync(CanalNotificacao.WhatsApp, tipo: TipoEventoNotificacao.PrazoEstourado);
        return await _s.SemearMensagemAsync(semente, CanalNotificacao.WhatsApp, ajustar: m =>
        {
            m.Remetente = OrigemRemetente.Plataforma;
            m.MarcarIndeterminado("timeout", "meta-plataforma");
        });
    }

    private static StatusPlataforma Sent(OutboxMensagemNotificacao m, Guid empresaDoOpaco) => new(
        NumeroPlataforma, "wamid.PLAT1", "sent", $"{empresaDoOpaco:N}.{m.Id:N}", null, null, null);

    [SkippableFact]
    public async Task OpacoFixaOTenantEFechaSoALinhaDaEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var deA = await SemearIndeterminadaAsync();
        var deB = await SemearIndeterminadaAsync();
        await using var provider = Webhook();
        using var escopo = provider.CreateScope();

        var ok = await escopo.ServiceProvider.GetRequiredService<ProcessarStatusWhatsAppPlataformaUseCase>()
            .ExecuteAsync(Sent(deA, deA.EmpresaId));

        ok.Should().BeTrue();
        var a = await _s.LerMensagemAsync(deA.Id);
        a.Status.Should().Be(StatusOutbox.Enviado);
        a.ProviderMensagemId.Should().Be("wamid.PLAT1");
        a.Remetente.Should().Be(OrigemRemetente.Plataforma);
        (await _s.LerMensagemAsync(deB.Id)).Status.Should().Be(StatusOutbox.Indeterminado);
    }

    [SkippableFact]
    public async Task OpacoDeOutraEmpresaNaoAtualizaNada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var deA = await SemearIndeterminadaAsync();
        var deB = await SemearIndeterminadaAsync();
        await using var provider = Webhook();
        using var escopo = provider.CreateScope();

        // O opaco diz empresa B com a mensagem de A: a RLS e o WHERE por empresa não deixam achar a linha.
        var ok = await escopo.ServiceProvider.GetRequiredService<ProcessarStatusWhatsAppPlataformaUseCase>()
            .ExecuteAsync(Sent(deA, deB.EmpresaId));

        ok.Should().BeTrue();
        (await _s.LerMensagemAsync(deA.Id)).Status.Should().Be(StatusOutbox.Indeterminado);
        (await _s.LerMensagemAsync(deB.Id)).Status.Should().Be(StatusOutbox.Indeterminado);
    }

    [SkippableFact]
    public async Task MigracaoAdicionaRemetenteComDefaultLoja()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await using var db = fixture.CreateDbContext();

        var coluna = await db.Database.SqlQuery<string>($"""
            SELECT column_default || '|' || is_nullable AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'notif_outbox_mensagens' AND column_name = 'Remetente'
            """).SingleAsync();

        coluna.Should().StartWith("'Loja'").And.EndWith("|NO");
    }

    [SkippableFact]
    public async Task EstadoDeTemplateEhGlobalELegivelSemTenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await using var provider = Webhook();
        using (var escrita = provider.CreateScope())
        {
            await escrita.ServiceProvider.GetRequiredService<ITemplateMetaEstadoRepository>()
                .GravarCategoriaAsync("prazo_estourado", "pt-BR", "MARKETING");
            await escrita.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        // Outro escopo, sem tenant, no papel NOBYPASSRLS: a tabela não tem EmpresaId nem policy.
        using var leitura = provider.CreateScope();
        var estado = await leitura.ServiceProvider.GetRequiredService<ITemplateMetaEstadoRepository>()
            .ObterAsync("prazo_estourado", "pt_BR");

        estado.Should().NotBeNull();
        estado!.EhMarketing.Should().BeTrue();

        await using var db = fixture.CreateDbContext();
        var comRls = await db.Database.SqlQuery<bool>($"""
            SELECT relrowsecurity AS "Value" FROM pg_class WHERE relname = 'notif_templates_meta_estado'
            """).SingleAsync();
        comRls.Should().BeFalse("o estado do template é da WABA, não de uma empresa");
    }
}
