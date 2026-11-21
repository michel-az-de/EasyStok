using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2695 (homologação #1769, U1): a conta interna do sync móvel tem vínculo ativo com a empresa, mas não é pessoa
/// da equipe. Não aparece em Configurações > Usuários nem ocupa vaga do limite de usuários do plano.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class UsuarioContaDeSistemaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Instante = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private async Task<(EasyStockDbContext Db, Guid EmpresaId)> CenarioAsync()
    {
        var empresaId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString, pg => pg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options;
        var db = new EasyStockDbContext(options);
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Conta de sistema", Documento = empresaId.ToString("N")[..14],
            CriadoEm = Instante, AlteradoEm = Instante
        });

        var ana = Usuario.Criar("Ana", $"ana+{empresaId:N}@casadababa.com", "hash");
        // Igual ao MobileSystemUserResolver: Ativo = false (trava de login) e vínculo ativo.
        var sync = Usuario.CriarContaDeSistema(empresaId);
        // Pessoa gravada com o domínio antes da guarda: o domínio sozinho não a esconde.
        var leo = new Usuario
        {
            Id = Guid.NewGuid(), Nome = "Leo", Email = $"leo+{empresaId:N}@system.local", SenhaHash = "$2a$11$hash",
            Ativo = true, CriadoEm = Instante, AlteradoEm = Instante
        };
        foreach (var u in new[] { ana, sync, leo })
        {
            db.Usuarios.Add(u);
            db.UsuariosEmpresas.Add(new UsuarioEmpresa { Id = Guid.NewGuid(), UsuarioId = u.Id, EmpresaId = empresaId, Ativo = true });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (db, empresaId);
    }

    [SkippableFact]
    public async Task Lista_de_usuarios_da_empresa_deixa_a_conta_de_sistema_de_fora()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var (db, empresaId) = await CenarioAsync();
        await using var _ = db;

        var (usuarios, total) = await new UsuarioRepository(db).GetByEmpresaAsync(empresaId, 1, 20);

        usuarios.Select(u => u.Nome).Should().BeEquivalentTo("Ana", "Leo");
        total.Should().Be(2, "a paginação conta só quem aparece");
    }

    [SkippableFact]
    public async Task Conta_de_sistema_nao_ocupa_vaga_do_plano()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var (db, empresaId) = await CenarioAsync();
        await using var _ = db;

        (await new UsuarioRepository(db).CountByEmpresaAsync(empresaId)).Should().Be(2);
    }
}
