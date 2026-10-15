using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// S05 em Postgres real: o lead criado pelo WhatsApp persiste com o telefone marcado como WhatsApp
/// e é reencontrado pelo hash na conversa seguinte; cadastro do ERP com o número sem máscara é
/// encontrado pelo <c>wa_id</c>.
/// </summary>
public class IdentificarClientePorTelefoneIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static IdentificarClientePorTelefoneUseCase CriarUseCase(Data.EasyStockDbContext db) =>
        new(new ClienteRepository(db), new ClienteStorefrontRepository(db),
            NullLogger<IdentificarClientePorTelefoneUseCase>.Instance);

    private async Task<Guid> SemearEmpresaAsync()
    {
        var empresa = Empresa.Criar("Casa da Baba " + Guid.NewGuid().ToString("N")[..6], null);
        await using var db = fixture.CreateDbContext();
        db.Set<Empresa>().Add(empresa);
        await db.SaveChangesAsync();
        return empresa.Id;
    }

    [SkippableFact]
    public async Task LeadCriadoEReencontradoPeloHash()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await SemearEmpresaAsync();
        const string waId = "5511988887777";

        Guid clienteId;
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var primeira = await CriarUseCase(db).ExecuteAsync(new IdentificarClientePorTelefoneInput(empresaId, waId, "Fulano"));
            await db.SaveChangesAsync();
            primeira.EhNovo.Should().BeTrue();
            clienteId = primeira.Cliente.Id;
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var telefone = await db.Set<ClienteTelefone>().SingleAsync(t => t.ClienteId == clienteId);
            telefone.Numero.Should().Be("+" + waId);
            telefone.Whatsapp.Should().BeTrue();
            telefone.Principal.Should().BeTrue();

            var segunda = await CriarUseCase(db).ExecuteAsync(new IdentificarClientePorTelefoneInput(empresaId, waId, "Fulano"));
            segunda.EhNovo.Should().BeFalse();
            segunda.EhLead.Should().BeTrue();
            segunda.Cliente.Id.Should().Be(clienteId);
        }
    }

    [SkippableFact]
    public async Task CadastroDoErpSemMascaraEncontradoPeloWaId()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await SemearEmpresaAsync();

        var cadastrado = Cliente.Criar(empresaId, "Maria Silva");
        cadastrado.Telefone = "11977776666";
        cadastrado.RegistrarPedido(DateTime.UtcNow);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            db.Clientes.Add(cadastrado);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var resultado = await CriarUseCase(db).ExecuteAsync(
                new IdentificarClientePorTelefoneInput(empresaId, "5511977776666", "Maria"));
            resultado.Cliente.Id.Should().Be(cadastrado.Id);
            resultado.EhNovo.Should().BeFalse();
            resultado.EhLead.Should().BeFalse();
        }
    }
}
