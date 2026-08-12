using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.RegistrarEmpresa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// KEYSTONE da issue #1024: o signup roda ANONIMO e CRIA o tenant, entao nao existe
/// <c>app.empresa_id</c> para o <c>SetTenantOnConnectionInterceptor</c> emitir. Sem bypass, a
/// policy <c>tenant_isolation</c> (WITH CHECK, ADR-0010) recusa os INSERTs em
/// <c>assinaturas_empresa</c>, <c>perfis</c>, <c>usuarios_empresas</c> e <c>usuarios_perfis</c>
/// com <c>42501</c>, e <c>POST /api/empresas/registrar</c> responde 500.
///
/// Roda como <c>rls_test_client</c> (NOSUPERUSER, NOBYPASSRLS). Com o <c>postgres</c> superuser
/// destes testes o RLS seria ignorado incondicionalmente e o teste passaria sem provar nada —
/// que e exatamente a duvida levantada na issue sobre a producao no Azure.
///
/// O <see cref="Controle_sem_bypass_a_policy_recusa_o_insert_da_assinatura"/> existe para que o
/// teste principal signifique alguma coisa: ele prova que a RLS esta VIVA neste ambiente. Sem
/// esse controle, um verde poderia significar apenas "a policy nao estava valendo".
/// </summary>
public class RegistrarEmpresaRlsIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Registra_empresa_completa_sob_role_NOSUPERUSER_NOBYPASSRLS()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        await SeedPlanoStarterAsync();

        await using var provider = BuildRlsClientProvider();

        RegistrarEmpresaResult result;
        await using (var scope = provider.CreateAsyncScope())
        {
            var useCase = scope.ServiceProvider.GetRequiredService<RegistrarEmpresaUseCase>();

            result = await useCase.ExecuteAsync(new RegistrarEmpresaCommand(
                NomeEmpresa: "Casa da Baba",
                Documento: "12345678000199",
                NomeAdmin: "Felipe",
                EmailAdmin: "felipe.azevedoit@outlook.com",
                SenhaAdmin: "senha-de-teste-123"));
        }

        result.EmpresaId.Should().NotBeEmpty();
        result.UsuarioId.Should().NotBeEmpty();

        // Confere pelo superuser: o que interessa e o que ficou GRAVADO, nao o que o use case
        // devolveu. As tres ultimas tabelas sao justamente as protegidas por RLS.
        //
        // IgnoreQueryFilters e obrigatorio aqui: este contexto de verificacao nao tem tenant, e o
        // Global Query Filter do EF (PRIMEIRA camada do ADR-0010) esconderia as linhas mesmo o
        // superuser ignorando a RLS do Postgres. Sem isto o assert da 0 e acusa um bug que nao
        // existe — as duas camadas precisam ser desligadas para inspecionar dado cross-tenant.
        await using var assert = fixture.CreateDbContext();

        (await assert.Set<Empresa>().CountAsync(e => e.Id == result.EmpresaId))
            .Should().Be(1, "a empresa e o tenant que acabou de nascer");
        (await assert.Set<AssinaturaEmpresa>().IgnoreQueryFilters()
            .CountAsync(a => a.EmpresaId == result.EmpresaId))
            .Should().Be(1, "assinaturas_empresa tem EmpresaId, logo tem RLS — e onde o 42501 estourava");
        (await assert.Set<UsuarioEmpresa>().IgnoreQueryFilters()
            .CountAsync(ue => ue.EmpresaId == result.EmpresaId))
            .Should().Be(1, "sem o vinculo o admin nao consegue logar na empresa que criou");
        (await assert.Set<UsuarioPerfil>().IgnoreQueryFilters()
            .CountAsync(up => up.EmpresaId == result.EmpresaId))
            .Should().Be(1, "sem o perfil o admin loga sem permissao nenhuma");
    }

    /// <summary>
    /// Controle negativo. Prova que a RLS esta ATIVA para o <c>rls_test_client</c> — sem isto o
    /// teste acima poderia estar verde por RLS desligada, nao pelo bypass.
    /// </summary>
    [SkippableFact]
    public async Task Controle_sem_bypass_a_policy_recusa_o_insert_da_assinatura()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var planoId = await SeedPlanoStarterAsync();

        // A empresa e criada pelo superuser: 'empresas' nao tem coluna EmpresaId, entao esta fora
        // da RLS. Isso importa para o controle ser honesto — sem uma empresa existente, o INSERT
        // da assinatura morreria de violacao de FK (23503) e nao provaria nada sobre a policy.
        var empresaId = Guid.NewGuid();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(Empresa.Criar("Empresa Controle", "99999999000199"));
            await seed.SaveChangesAsync();
            empresaId = await seed.Set<Empresa>()
                .Where(e => e.Documento == "99999999000199").Select(e => e.Id).SingleAsync();
        }

        await using var provider = BuildRlsClientProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        // Nenhum tenant no contexto e nenhum bypass: o interceptor emite app.empresa_id = '',
        // NULLIF vira NULL e o WITH CHECK da tenant_isolation reprova.
        var agora = DateTime.UtcNow;
        db.Set<AssinaturaEmpresa>().Add(new AssinaturaEmpresa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            PlanoId = planoId,
            DataInicio = agora,
            Status = StatusAssinatura.Ativa,
            CriadoEm = agora,
            AlteradoEm = agora
        });

        var act = async () => await db.SaveChangesAsync();

        var ex = await act.Should().ThrowAsync<DbUpdateException>(
            "sem bypass a policy tenant_isolation tem que recusar — se este teste ficar verde, " +
            "a RLS nao esta valendo e o teste principal desta classe nao prova nada");

        ex.Which.InnerException.Should().BeOfType<PostgresException>()
          .Which.SqlState.Should().Be("42501", "e o codigo de violacao de RLS do Postgres");
    }

    private async Task<Guid> SeedPlanoStarterAsync()
    {
        var planoId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Set<Plano>().Add(new Plano
        {
            Id = planoId,
            Nome = "Starter",
            PrecoMensal = 100m,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        return planoId;
    }

    /// <summary>
    /// Mesmo grafo de DI da producao, porem apontado para o login NOSUPERUSER/NOBYPASSRLS. E o
    /// container real que amarra IRowLevelSecurityBypass -> RowLevelSecurityBypass -> DbContext;
    /// montar o use case na mao pularia justamente a fiacao que esta issue conserta.
    /// </summary>
    private ServiceProvider BuildRlsClientProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<ICacheService>());
        // Mesmo registro da producao (AddEasyStockAsyncInfrastructure), so que sozinho: trazer a
        // infra Async inteira arrastaria backend de cache e config que nada tem a ver com RLS.
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
