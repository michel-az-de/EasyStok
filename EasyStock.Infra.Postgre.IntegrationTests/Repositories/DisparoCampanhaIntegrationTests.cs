using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Queries;
using EasyStock.Infra.Postgre.Repositories.Campanhas;
using EasyStock.Infra.Postgre.Repositories.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// S30 em Postgres real, no escopo do tenant como o job roda: a primeira onda acha o template global,
/// grava o outbox com <c>ProximaTentativaEm = DisparoEm</c> e categoria marketing, o job concilia o
/// envio e conclui a onda, a lista cross-tenant do job e a atribuição do pedido.
/// </summary>
public class DisparoCampanhaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task PrimeiraOndaNoOutboxConciliacaoEAtribuicao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Casa da Baba Disparo", "88888888000191");
        var clientes = new[] { "Ana", "Bia" }.Select(nome =>
        {
            var cliente = Cliente.Criar(empresa.Id, nome);
            cliente.Telefone = "(11) 99757-3992";
            cliente.DefinirConsentimentoMarketing(true, agora);
            return cliente;
        }).ToArray();
        var campanha = Campanha.Criar(empresa.Id, Guid.NewGuid(),
            new DadosCampanha("Bolo de fubá", "Oi {{nome}}, saiu bolo!", null, null, FiltroCampanha.ParaTodos, [],
                null, false, 1),
            agora.AddHours(-2));
        campanha.Agendar(agora.AddHours(-1), agora.AddHours(-2));

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.Add(empresa);
            db.Clientes.AddRange(clientes);
            db.Campanhas.Add(campanha);
            await db.SaveChangesAsync();
        }

        await SemearTemplateGlobalAsync();
        await using (var db = TenantDb(empresa.Id))
        {
            await new CalcularPublicoCampanhaUseCase(new CampanhaRepository(db), new CampanhaPublicoQueries(db), db, TimeProvider.System)
                .ExecuteAsync(empresa.Id, campanha.Id);
        }

        // Lista cross-tenant do job: agendada vencida.
        (await ListarParaProcessarAsync()).Should().Contain(new CampanhaParaProcessar(empresa.Id, campanha.Id));

        var rodada = await RodarJobAsync(empresa.Id, campanha.Id);
        rodada.OndaDisparada.Should().Be(1);

        OutboxMensagemNotificacao mensagem;
        await using (var db = TenantDb(empresa.Id))
        {
            var enfileirado = await db.CampanhaDestinatarios
                .SingleAsync(d => d.CampanhaId == campanha.Id && d.Status == StatusCampanhaDestinatario.Enfileirado);
            mensagem = await db.NotifOutboxMensagens.SingleAsync(m => m.Id == enfileirado.OutboxMensagemId);
            mensagem.Categoria.Should().Be(CategoriaConteudoNotificacao.Marketing);
            mensagem.Canal.Should().Be(CanalNotificacao.WhatsApp);
            mensagem.ProximaTentativaEm.Should().BeCloseTo(campanha.DisparoEm!.Value, TimeSpan.FromMilliseconds(1));
            mensagem.Destinatario.Should().Be("+5511997573992");
            mensagem.CorpoRenderizado.Should().Be("Oi Ana, saiu bolo!");
            mensagem.LerMetadados().Should().Contain("template", EnfileiradorMensagensCampanha.TemplateMetaGenerico);
            (await db.CampanhaDestinatarios.CountAsync(d => d.CampanhaId == campanha.Id
                && d.Status == StatusCampanhaDestinatario.Pendente)).Should().Be(1, "onda de 1: Bia fica para a dona");
        }

        // O dispatcher mandou: o job concilia e conclui a onda; a segunda não sai sozinha.
        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            var noBanco = await db.NotifOutboxMensagens.IgnoreQueryFilters().SingleAsync(m => m.Id == mensagem.Id);
            noBanco.MarcarEnviado("whatsapp:meta");
            await db.SaveChangesAsync();
        }

        var conciliacao = await RodarJobAsync(empresa.Id, campanha.Id);
        conciliacao.Enviados.Should().Be(1);
        conciliacao.OndaConcluida.Should().BeTrue();
        conciliacao.OndaDisparada.Should().BeNull();
        (await ListarParaProcessarAsync()).Should().NotContain(new CampanhaParaProcessar(empresa.Id, campanha.Id),
            "enviada, sem encerramento e sem ninguém na fila: nada a fazer");

        await using (var db = TenantDb(empresa.Id))
        {
            var repo = new CampanhaRepository(db);
            var enviado = await repo.ObterEnviadoParaAtribuirAsync(empresa.Id, clientes[0].Id, DateTime.UtcNow.AddDays(-7));
            enviado.Should().NotBeNull();
            enviado!.EnviadoEm.Should().NotBeNull();
            (await repo.ObterEnviadoParaAtribuirAsync(Guid.NewGuid(), clientes[0].Id, DateTime.UtcNow.AddDays(-7)))
                .Should().BeNull("EmpresaId no WHERE");
            (await repo.ObterEnviadoParaAtribuirAsync(empresa.Id, clientes[1].Id, DateTime.UtcNow.AddDays(-7)))
                .Should().BeNull("Bia não recebeu");

            enviado.RegistrarPedido(Guid.NewGuid());
            await db.SaveChangesAsync();
            (await repo.ObterEnviadoParaAtribuirAsync(empresa.Id, clientes[0].Id, DateTime.UtcNow.AddDays(-7)))
                .Should().BeNull("só o primeiro pedido conta");
        }
    }

    [SkippableFact]
    public async Task DuasCampanhasNoMesmoTickNaoAtingemOMesmoCliente()
    {
        // #1292 (RN-40): a primeira campanha deixa Ana Enfileirada (ainda sem EnviadoEm); a segunda, no mesmo
        // tick do job, precisa enxergar esse envio e tirá-la pelo limite semanal.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Casa da Baba Limite Semanal", "12121212000191");
        var ana = Cliente.Criar(empresa.Id, "Ana");
        ana.Telefone = "(11) 99757-3992";
        ana.DefinirConsentimentoMarketing(true, agora);
        Campanha NovaAgendada(string nome)
        {
            var campanha = Campanha.Criar(empresa.Id, Guid.NewGuid(),
                new DadosCampanha(nome, "Oi {{nome}}!", null, null, FiltroCampanha.ParaTodos, [], null, false, null),
                agora.AddHours(-2));
            campanha.Agendar(agora.AddHours(-1), agora.AddHours(-2));
            return campanha;
        }
        var primeira = NovaAgendada("Bolo de fubá");
        var segunda = NovaAgendada("Pão de mel");

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.Add(empresa);
            db.Clientes.Add(ana);
            db.Campanhas.AddRange(primeira, segunda);
            await db.SaveChangesAsync();
        }

        await SemearTemplateGlobalAsync();
        foreach (var campanha in new[] { primeira, segunda })
        {
            await using var db = TenantDb(empresa.Id);
            await new CalcularPublicoCampanhaUseCase(new CampanhaRepository(db), new CampanhaPublicoQueries(db), db, TimeProvider.System)
                .ExecuteAsync(empresa.Id, campanha.Id);
        }

        (await RodarJobAsync(empresa.Id, primeira.Id)).OndaDisparada.Should().Be(1);
        await RodarJobAsync(empresa.Id, segunda.Id);

        await using (var db = TenantDb(empresa.Id))
        {
            var naSegunda = await db.CampanhaDestinatarios.SingleAsync(d => d.CampanhaId == segunda.Id);
            naSegunda.Status.Should().Be(StatusCampanhaDestinatario.Excluido, "Ana já está na fila da primeira campanha");
            naSegunda.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.LimiteSemanal);
            (await db.NotifOutboxMensagens.CountAsync(m => m.Destinatario == "+5511997573992")).Should().Be(1);
        }
    }

    [SkippableFact]
    public async Task TemplateGlobalApareceNoEscopoDaEmpresaMesmoComRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await SemearTemplateGlobalAsync();

        // Como a API em produção: login sujeito a RLS, interceptor emitindo o tenant da empresa.
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        await using var db = new EasyStockDbContext(options);
        db.SetMobileTenantContext(Guid.NewGuid());

        (await db.NotifTemplates.IgnoreQueryFilters().AnyAsync(t => t.EmpresaId == null))
            .Should().BeTrue("N1: a policy de SELECT do catálogo global expõe a linha global ao tenant, sem bypass");

        var template = await new TemplateNotificacaoRepository(db)
            .GetAtivoAsync(EnfileiradorMensagensCampanha.CodigoTemplateOnda, CanalNotificacao.WhatsApp, null);

        template.Should().NotBeNull("o fallback para o template global precisa funcionar no escopo da empresa");
        template!.EmpresaId.Should().BeNull();
        db.BypassRowLevelSecurity.Should().BeFalse("o repositório não liga bypass para ler o global (N1)");
    }

    private async Task SemearTemplateGlobalAsync()
    {
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        if (await db.NotifTemplates.IgnoreQueryFilters().AnyAsync(t =>
                t.Codigo == EnfileiradorMensagensCampanha.CodigoTemplateOnda && t.EmpresaId == null))
            return;

        var template = TemplateNotificacao.Criar(EnfileiradorMensagensCampanha.CodigoTemplateOnda, "Campanha",
            CanalNotificacao.WhatsApp, TipoEventoNotificacao.CampanhaMarketing, "", "{{ mensagem }}");
        template.Aprovar("system");
        template.Ativar();
        db.NotifTemplates.Add(template);
        await db.SaveChangesAsync();
    }

    private EasyStockDbContext TenantDb(Guid empresaId)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return db;
    }

    private async Task<IReadOnlyList<CampanhaParaProcessar>> ListarParaProcessarAsync()
    {
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        return await new CampanhaRepository(db).ListarParaProcessarAsync(DateTime.UtcNow, 500);
    }

    private async Task<ProcessamentoCampanhaResult> RodarJobAsync(Guid empresaId, Guid campanhaId)
    {
        await using var db = TenantDb(empresaId);
        var repo = new CampanhaRepository(db);
        var queries = new CampanhaPublicoQueries(db);
        var enfileirador = new EnfileiradorMensagensCampanha(
            new TemplateNotificacaoRepository(db), new RendererChaves(), new EventoNotificacaoRepository(db),
            new OutboxNotificacaoRepository(db), new BloqueioNotificacaoRepository(db));
        var disparar = new DispararOndaCampanhaUseCase(repo, queries, enfileirador, db, TimeProvider.System);
        return await new ProcessarCampanhaUseCase(repo, queries, disparar, enfileirador, db, TimeProvider.System)
            .ExecuteAsync(empresaId, campanhaId);
    }

    private sealed class RendererChaves : IRendererTemplate
    {
        public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, CancellationToken ct = default) =>
            RenderizarAsync(template, variaveis, false, ct);

        public Task<string> RenderizarAsync(string template, IDictionary<string, object?> variaveis, bool htmlEscape, CancellationToken ct = default) =>
            Task.FromResult(Regex.Replace(template, @"\{\{\s*(\w+)\s*\}\}",
                m => variaveis.TryGetValue(m.Groups[1].Value, out var v) ? v?.ToString() ?? string.Empty : string.Empty));
    }
}
