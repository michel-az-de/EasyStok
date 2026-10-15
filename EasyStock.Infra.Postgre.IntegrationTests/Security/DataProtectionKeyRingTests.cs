using System.Security.Cryptography;
using System.Text;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Security;

/// <summary>
/// A Api cifra o certificado A1 (.pfx + senha) e o token do gateway fiscal; o Worker
/// decifra os mesmos bytes ao emitir e ao reprocessar NFC-e. Como sao processos
/// separados (containers <c>ez-api</c> e <c>ez-worker</c>), o <c>IDataProtector</c> so
/// fecha o ciclo se os dois lerem o MESMO key ring.
///
/// <para>
/// Antes de #1035 cada host usava o key ring default do proprio processo, e o
/// <c>Unprotect</c> cruzado estourava
/// <c>CryptographicException: The key {...} was not found in the key ring</c>.
/// </para>
/// </summary>
public class DataProtectionKeyRingTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    // Mesmo purpose que NfeCertificadoA1Service usa em producao (prefixo + kekId "v1").
    private const string PurposeCertA1 = "EasyStock.Fiscal.NfeCertificadoA1.v1.v1";

    [SkippableFact]
    public void Worker_decifra_o_que_a_Api_cifrou()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var segredo = Encoding.UTF8.GetBytes("pfx-bytes-e-senha-do-certificado-A1");

        using var api = BuildHost(fixture.ConnectionString);
        using var worker = BuildHost(fixture.ConnectionString);

        var cifrado = api.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(PurposeCertA1)
            .Protect(segredo);

        var decifrar = () => worker.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(PurposeCertA1)
            .Unprotect(cifrado);

        decifrar.Should().NotThrow<CryptographicException>(
            "Api e Worker precisam compartilhar o key ring, senao o certificado A1 gravado " +
            "em credencial_integracao.payload_cifrado e ilegivel para a emissao fiscal");

        decifrar().Should().Equal(segredo);
    }

    /// <summary>
    /// O key ring e lido fora de qualquer escopo de tenant. Se <c>data_protection_keys</c>
    /// ganhasse uma coluna <c>EmpresaId</c>, a migration <c>AddRowLevelSecurity</c> passaria
    /// a trancar a tabela e os dois hosts parariam de enxergar as proprias chaves — este
    /// teste existe para que essa regressao apareca aqui, e nao na emissao fiscal.
    /// </summary>
    [SkippableFact]
    public async Task Key_ring_e_legivel_sem_contexto_de_tenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        using var host = BuildHost(fixture.ConnectionString);
        host.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(PurposeCertA1)
            .Protect(Encoding.UTF8.GetBytes("forca a criacao de ao menos uma chave"));

        // Login NOSUPERUSER, sem SET app.empresa_id: tabela com RLS retornaria 0 linhas.
        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();

        var chaves = await ctx.DataProtectionKeys.AsNoTracking().CountAsync();
        chaves.Should().BeGreaterThan(0, "a tabela de chaves e global e nao pode cair no RLS de tenant");
    }

    /// <summary>
    /// Monta um host isolado, como um container separado: <see cref="ServiceProvider"/>
    /// proprio, nenhum estado de DataProtection compartilhado em memoria.
    /// </summary>
    private static ServiceProvider BuildHost(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<EasyStockDbContext>(o => o.UseNpgsql(connectionString));
        services.AddEasyStockDataProtection();

        return services.BuildServiceProvider();
    }
}
