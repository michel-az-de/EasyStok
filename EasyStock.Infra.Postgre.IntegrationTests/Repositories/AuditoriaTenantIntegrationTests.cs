using Microsoft.EntityFrameworkCore;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public sealed class AuditoriaTenantIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task UsuarioCompartilhado_NaoExpoeEventoDeOutraEmpresaNemLegadoSemOrigem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var a = Empresa.Criar("Loja A", "11111111000191");
        var b = Empresa.Criar("Loja B", "22222222000191");
        var usuario = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@exemplo.com", "hash");
        await using var db = fixture.CreateDbContext();
        db.Empresas.AddRange(a, b);
        db.Usuarios.Add(usuario);
        foreach (var empresa in new[] { a, b })
            db.UsuariosEmpresas.Add(new UsuarioEmpresa
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresa.Id,
                Ativo = true, CriadoEm = DateTime.UtcNow
            });
        var eventoA = AuditLog.Criar(usuario.Id, "perfil-trocado", true, "Loja A", null, null);
        eventoA.EmpresaId = a.Id;
        var eventoB = AuditLog.Criar(usuario.Id, "perfil-trocado", true, "Loja B", null, null);
        eventoB.EmpresaId = b.Id;
        var legado = AuditLog.Criar(usuario.Id, "login", true, "Sem origem", null, null);
        db.AuditLogs.AddRange(eventoA, eventoB, legado);
        await db.SaveChangesAsync();
        db.SetMobileTenantContext(a.Id);
        var repo = new AuditLogRepository(db);
        var resultado = await repo.ListarPorEmpresaAsync(a.Id, usuario.Id, 1, 20);
        resultado.Total.Should().Be(1);
        resultado.Itens.Should().ContainSingle().Which.Id.Should().Be(eventoA.Id);
        db.SetMobileTenantContext(b.Id);
        var outra = await repo.ListarPorEmpresaAsync(b.Id, usuario.Id, 1, 20);
        outra.Total.Should().Be(1);
        outra.Itens.Should().ContainSingle().Which.Id.Should().Be(eventoB.Id);
        await using var rls = fixture.CreateRlsClientDbContext();
        await rls.Database.OpenConnectionAsync();
        await rls.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {a.Id.ToString()}, false)");
        var visiveis = await rls.AuditLogs.IgnoreQueryFilters().Where(x => x.UsuarioId == usuario.Id).ToListAsync();
        visiveis.Should().ContainSingle().Which.Id.Should().Be(eventoA.Id);
        var indevido = AuditLog.Criar(usuario.Id, "perfil-trocado", true, "Outra loja", null, null);
        indevido.EmpresaId = b.Id;
        rls.AuditLogs.Add(indevido);
        await rls.Invoking(x => x.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        rls.ChangeTracker.Clear();
        rls.AuditLogs.Add(AuditLog.Criar(usuario.Id, "login", true, "Evento sem tenant", null, null));
        await rls.SaveChangesAsync();

    }
}
