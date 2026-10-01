using System.Security.Cryptography;
using System.Text;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Integration;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Integration;

/// <summary>
/// F16 (#1246): a chave da loja em <c>credencial_integracao</c> com Postgres real. Grava cifrada
/// (o texto não aparece em <c>payload_cifrado</c>), volta pelo resolver, o cache não sobrevive a
/// desativar, e a policy de RLS esconde a linha de outra empresa. A KEK é gerada no teste.
/// </summary>
public class CredencialIntegracaoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Segredo = "APP_USR-segredo-de-teste-9876";
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private sealed record ChaveTeste(string AccessToken);

    private static IConfiguration ConfigComKek(string kekId, string kekBase64, string? outraId = null, string? outra = null)
    {
        var valores = new Dictionary<string, string?>
        {
            ["Crypto:CurrentKekId"] = kekId,
            [$"Crypto:Keks:{kekId}"] = kekBase64,
        };
        if (outraId is not null) valores[$"Crypto:Keks:{outraId}"] = outra;
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static string KekNova() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private IntegrationCredentialResolver Resolver(EasyStockDbContext db, IConfiguration config) =>
        new(new CredencialIntegracaoRepository(db), db, config, _cache, NullLogger<IntegrationCredentialResolver>.Instance);

    private static async Task CriarEmpresaAsync(EasyStockDbContext db, Guid empresaId)
    {
        var empresa = Empresa.Criar($"Empresa {empresaId:N}", null);
        empresa.Id = empresaId;
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task SalvarGravaCifradoSemOTextoEVoltaPeloResolver()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var config = ConfigComKek("kek-a", KekNova());
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        await CriarEmpresaAsync(db, empresa);

        await Resolver(db, config).SalvarAsync(empresa, CategoriaIntegracao.Payments, "mercadopago",
            AmbienteIntegracao.Production, new ChaveTeste(Segredo), Guid.NewGuid(), mascara: "9876");

        var bruto = await db.Database
            .SqlQuery<byte[]>($"SELECT payload_cifrado AS \"Value\" FROM credencial_integracao WHERE empresa_id = {empresa}")
            .SingleAsync();
        Encoding.UTF8.GetString(bruto).Should().NotContain("segredo", "o payload é cifrado com AES-256-GCM");
        bruto.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Segredo)).Should().Be(-1);

        var linha = await db.CredenciaisIntegracao.AsNoTracking().SingleAsync(c => c.EmpresaId == empresa);
        linha.Mascara.Should().Be("9876");
        linha.KekId.Should().Be("kek-a");

        await using var outroDb = fixture.CreateDbContext();
        outroDb.SetMobileTenantContext(empresa);
        var lida = await Resolver(outroDb, config).ObterAsync<ChaveTeste>(empresa, "mercadopago", AmbienteIntegracao.Production);
        lida!.AccessToken.Should().Be(Segredo);
    }

    [SkippableFact]
    public async Task DesativarInvalidaOCache()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var config = ConfigComKek("kek-a", KekNova());
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        await CriarEmpresaAsync(db, empresa);
        var resolver = Resolver(db, config);
        await resolver.SalvarAsync(empresa, CategoriaIntegracao.Logistics, "lalamove",
            AmbienteIntegracao.Sandbox, new ChaveTeste(Segredo), Guid.NewGuid());
        (await resolver.ObterAsync<ChaveTeste>(empresa, "lalamove", AmbienteIntegracao.Sandbox)).Should().NotBeNull();

        (await resolver.DesativarAsync(empresa, "lalamove")).Should().Be(1);

        (await resolver.ObterAsync<ChaveTeste>(empresa, "lalamove", AmbienteIntegracao.Sandbox))
            .Should().BeNull("a chave desativada não pode continuar saindo do cache por 5 min");
    }

    [SkippableFact]
    public async Task RotacionarRecifraComANovaKekEInvalidaOCache()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var kekA = KekNova();
        var kekB = KekNova();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        await CriarEmpresaAsync(db, empresa);
        var resolverA = Resolver(db, ConfigComKek("kek-rot-a", kekA));
        await resolverA.SalvarAsync(empresa, CategoriaIntegracao.Mapas, "googlemaps",
            AmbienteIntegracao.Production, new ChaveTeste(Segredo), Guid.NewGuid());
        await resolverA.ObterAsync<ChaveTeste>(empresa, "googlemaps", AmbienteIntegracao.Production);

        await Resolver(db, ConfigComKek("kek-rot-b", kekB, "kek-rot-a", kekA)).RotacionarKekAsync("kek-rot-b");

        // Só a KEK nova configurada: a leitura tem de vir do banco, recifrada, e não do cache.
        await using var depois = fixture.CreateDbContext();
        depois.SetMobileTenantContext(empresa);
        var linha = await depois.CredenciaisIntegracao.AsNoTracking().SingleAsync(c => c.EmpresaId == empresa);
        linha.KekId.Should().Be("kek-rot-b");
        var lida = await Resolver(depois, ConfigComKek("kek-rot-b", kekB))
            .ObterAsync<ChaveTeste>(empresa, "googlemaps", AmbienteIntegracao.Production);
        lida!.AccessToken.Should().Be(Segredo);
    }

    [SkippableFact]
    public async Task RlsEscondeACredencialDeOutraEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var config = ConfigComKek("kek-a", KekNova());
        foreach (var empresa in new[] { empresaA, empresaB })
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa);
            await CriarEmpresaAsync(db, empresa);
            await Resolver(db, config).SalvarAsync(empresa, CategoriaIntegracao.Payments, "mercadopago",
                AmbienteIntegracao.Production, new ChaveTeste(Segredo), Guid.NewGuid());
        }

        // Login comum (NOSUPERUSER, NOBYPASSRLS): só a policy do banco separa as empresas.
        await using var cliente = fixture.CreateRlsClientDbContext();
        await cliente.Database.OpenConnectionAsync();
        await cliente.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresaA.ToString()}, false)");

        var deB = await cliente.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM credencial_integracao WHERE empresa_id = {empresaB}")
            .SingleAsync();
        var deA = await cliente.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM credencial_integracao WHERE empresa_id = {empresaA}")
            .SingleAsync();

        deB.Should().Be(0, "a credencial de outra empresa é invisível pela policy tenant_isolation");
        deA.Should().Be(1);
    }
}
