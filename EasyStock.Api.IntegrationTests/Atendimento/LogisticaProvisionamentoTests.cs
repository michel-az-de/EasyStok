using System.Net;
using System.Net.Http.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    [SkippableFact]
    public async Task Logistica_ProvisionamentoDemo_RestritoAoSuperAdmin_GravaNovoTenantComRls()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(nivel: NivelAcesso.SuperAdmin);
        using var client = factory.CreateClient();
        await SemearAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            db.Usuarios.Add(new Usuario { Id = UsuarioId, Nome = "Administrador de teste", Email = "admin-logistica@fixture.test", SenhaHash = "sem-login-externo", Ativo = true, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            if (!await db.Planos.AnyAsync(p => p.Nome == "Starter"))
                db.Planos.Add(new Plano { Id = Guid.NewGuid(), Nome = "Starter", Ativo = true, CriadoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var corpo = new { nomeEmpresa = "Demonstração de entregas", nomeAdmin = "Gerente teste", emailAdmin = "demo-logistica@fixture.test", enviarEmail = false, motivo = "Provisionamento de demonstração isolada autorizada para teste" };
        var resultado = await LogisticaOk(client.PostAsJsonAsync("/api/admin/tenants", corpo));
        var novaEmpresa = resultado.GetProperty("tenantId").GetGuid();
        novaEmpresa.Should().NotBe(EmpresaId);
        resultado.GetProperty("emailEnviado").GetBoolean().Should().BeFalse();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(novaEmpresa);
            (await db.AssinaturasEmpresa.AnyAsync(a => a.EmpresaId == novaEmpresa)).Should().BeTrue();
            (await db.UsuariosEmpresas.AnyAsync(u => u.EmpresaId == novaEmpresa)).Should().BeTrue();
        }
        await using var atendenteFactory = CriarFactory(nivel: NivelAcesso.Operador);
        using var atendente = atendenteFactory.CreateClient();
        (await atendente.PostAsJsonAsync("/api/admin/tenants", corpo)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
