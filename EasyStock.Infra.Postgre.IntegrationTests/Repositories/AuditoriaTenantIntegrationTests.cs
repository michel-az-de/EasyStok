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
    }
}
