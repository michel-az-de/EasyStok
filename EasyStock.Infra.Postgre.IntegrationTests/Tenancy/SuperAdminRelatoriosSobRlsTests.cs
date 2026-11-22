using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Reporting;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories.Reporting;
using EasyStock.Infra.Postgre.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// <c>report_runs</c> sob RLS (#3108): o SuperAdmin enfileira run de plataforma (sem empresa) e lê ou cancela run de
/// outra empresa, sempre fora do próprio tenant. Sem bypass a policy barra a gravação e esconde a leitura. Roda como
/// <c>rls_test_client</c>, com o interceptor que leva tenant e bypass à sessão.
/// </summary>
public sealed class SuperAdminRelatoriosSobRlsTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task SuperAdmin_enfileira_lista_le_e_cancela_run_fora_do_proprio_tenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await fixture.ResetDatabaseAsync();

        var superAdmin = Substitute.For<ICurrentUserAccessor>();
        superAdmin.Nivel.Returns(NivelAcesso.SuperAdmin);
        superAdmin.UsuarioId.Returns(Guid.NewGuid());
        superAdmin.EmpresaId.Returns(Guid.NewGuid());

        var deOutraEmpresa = ReportRun.Enqueue(Guid.NewGuid(), Guid.NewGuid(), "vendas.teste", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash-outra", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        await using (var seed = fixture.CreateDbContext())
        {
            await seed.Database.OpenConnectionAsync();
            await seed.Database.ExecuteSqlRawAsync("SET session_replication_role = replica"); // empresa fictícia
            seed.ReportRuns.Add(deOutraEmpresa);
            await seed.SaveChangesAsync();
        }

        var definicao = Substitute.For<IReportDefinition>();
        definicao.Key.Returns("plataforma.teste");
        definicao.Contexto.Returns(ReportContexto.AdminSaaS);
        definicao.Categoria.Returns(ReportCategoria.Vendas);
        definicao.FormatosSuportados.Returns([ReportFormat.Csv]);
        definicao.Retencao.Returns(TimeSpan.FromDays(7));
        definicao.MaxTentativas.Returns(3);
        definicao.SemanticVersion.Returns("1.0.0");
        var registry = new ReportRegistry([definicao]);

        await using var db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.RlsClientConnectionString)
                .AddInterceptors(new SetTenantOnConnectionInterceptor()).Options,
            superAdmin);
        var repo = new ReportRunRepository(db);
        var rls = new RowLevelSecurityBypass(db);

        var enfileirada = await new EnqueueReportRunUseCase(repo, registry, superAdmin, rls).ExecuteAsync(
            new EnqueueReportRunCommand("plataforma.teste", "{}", ReportFormat.Csv, MotivoExecucao: "teste de RLS"));
        enfileirada.JaExistia.Should().BeFalse();

        var lista = await new ListMyReportRunsUseCase(repo, registry, superAdmin, rls).ExecuteAsync(
            new ListMyReportRunsQuery(new ReportListFilter()));
        lista.Select(r => r.Id).Should().Contain(enfileirada.Run.Id, "a run de plataforma não tem empresa");

        var lida = await new GetReportRunUseCase(repo, registry, Substitute.For<IRelatorioDownloadLink>(), superAdmin, rls)
            .ExecuteAsync(new GetReportRunQuery(deOutraEmpresa.Id));
        lida.Should().NotBeNull("o SuperAdmin lê run de qualquer empresa");

        (await new CancelReportRunUseCase(repo, superAdmin, rls).ExecuteAsync(new CancelReportRunCommand(deOutraEmpresa.Id)))
            .Should().BeTrue("o SuperAdmin cancela run de qualquer empresa");

        db.BypassRowLevelSecurity.Should().BeFalse("o bypass termina com o caso de uso");
    }

    [SkippableFact]
    public async Task Usuario_comum_nao_ganha_bypass_e_nao_le_run_de_outra_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        await fixture.ResetDatabaseAsync();

        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.Nivel.Returns(NivelAcesso.Admin);
        usuario.UsuarioId.Returns(Guid.NewGuid());
        usuario.EmpresaId.Returns(Guid.NewGuid());

        // Mesmo usuário solicitante, outra empresa: o filtro do caso de uso de cancelamento só olha o usuário.
        var deOutraEmpresa = ReportRun.Enqueue(Guid.NewGuid(), usuario.UsuarioId, "vendas.teste", ReportCategoria.Vendas,
            ReportContexto.Tenant, "{}", "hash-outra", ReportFormat.Csv, "1.0.0", TimeSpan.FromDays(7));
        await using (var seed = fixture.CreateDbContext())
        {
            await seed.Database.OpenConnectionAsync();
            await seed.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
            seed.ReportRuns.Add(deOutraEmpresa);
            await seed.SaveChangesAsync();
        }

        await using var db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.RlsClientConnectionString)
                .AddInterceptors(new SetTenantOnConnectionInterceptor()).Options,
            usuario);
        var repo = new ReportRunRepository(db);

        // Espião no lugar do bypass real: nenhum dos quatro casos de uso pode sequer pedi-lo para quem não é SuperAdmin.
        var espiao = Substitute.For<EasyStock.Application.Ports.Output.Security.IRowLevelSecurityBypass>();
        var definicao = Substitute.For<IReportDefinition>();
        definicao.Key.Returns("vendas.teste");
        definicao.FormatosSuportados.Returns([ReportFormat.Csv]);
        var registry = new ReportRegistry([definicao]);

        (await new CancelReportRunUseCase(repo, usuario, espiao).ExecuteAsync(new CancelReportRunCommand(deOutraEmpresa.Id)))
            .Should().BeFalse("a policy esconde a run de outra empresa de quem não é SuperAdmin");
        (await new GetReportRunUseCase(repo, registry, Substitute.For<IRelatorioDownloadLink>(), usuario, espiao)
                .ExecuteAsync(new GetReportRunQuery(deOutraEmpresa.Id)))
            .Should().BeNull("a policy esconde a run de outra empresa");
        (await new ListMyReportRunsUseCase(repo, registry, usuario, espiao).ExecuteAsync(
                new ListMyReportRunsQuery(new ReportListFilter())))
            .Should().BeEmpty("o usuário não tem run na própria empresa");
        var enfileirar = () => new EnqueueReportRunUseCase(repo, registry, usuario, espiao).ExecuteAsync(
            new EnqueueReportRunCommand("vendas.teste", "{}", ReportFormat.Csv));
        await enfileirar.Should().ThrowAsync<UnauthorizedAccessException>();

        espiao.DidNotReceiveWithAnyArgs().Begin();
        await espiao.DidNotReceiveWithAnyArgs().BeginAsync(default);
        db.BypassRowLevelSecurity.Should().BeFalse();
    }
}
