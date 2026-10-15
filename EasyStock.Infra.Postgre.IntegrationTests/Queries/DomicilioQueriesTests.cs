using EasyStock.Domain.Entities;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Queries;

/// <summary>
/// Sinal de mesmo domicílio (S25, RN-13, D10) em Postgres real: mesmo <c>cep+numero+complemento</c>
/// normalizado aproxima dois cadastros só por id e nome; endereço diferente, outra empresa ou cadastro
/// inativo não aparecem.
/// </summary>
public class DomicilioQueriesTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task MesmoEnderecoSemHistorico()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresa = Empresa.Criar("Casa da Baba Domicilio", "22222222000191");
        var outraEmpresa = Empresa.Criar("Outra Domicilio", "33333333000191");
        var maria = Cliente.Criar(empresa.Id, "Maria");
        var joao = Cliente.Criar(empresa.Id, "João");
        var vizinha = Cliente.Criar(empresa.Id, "Vizinha do 13");
        var inativo = Cliente.Criar(empresa.Id, "Inativo");
        inativo.Desativar();
        var deOutraEmpresa = Cliente.Criar(outraEmpresa.Id, "De outra empresa");

        Endereco(maria, "05500-000", "12", "Apto 3");
        Endereco(joao, "05500000", " 12 ", "apto 3");
        Endereco(vizinha, "05500000", "12", "Apto 4");
        Endereco(inativo, "05500000", "12", "apto 3");
        Endereco(deOutraEmpresa, "05500000", "12", "apto 3");

        // João tem histórico: nada disso pode vir no domicílio da Maria.
        var pedidoJoao = Pedido.Criar(empresa.Id, joao);

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await db.Database.MigrateAsync();
            db.Empresas.AddRange(empresa, outraEmpresa);
            db.Clientes.AddRange(maria, joao, vizinha, inativo, deOutraEmpresa);
            db.Pedidos.Add(pedidoJoao);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var queries = new DomicilioQueries(db);

            (await queries.ListarMesmoDomicilioAsync(empresa.Id, maria.Id))
                .Should().Equal(new ClienteMesmoDomicilio(joao.Id, "João"));
            (await queries.ListarMesmoDomicilioAsync(empresa.Id, joao.Id))
                .Should().Equal(new ClienteMesmoDomicilio(maria.Id, "Maria"));
            (await queries.ListarMesmoDomicilioAsync(empresa.Id, vizinha.Id))
                .Should().BeEmpty("complemento diferente é outro domicílio");
        }

        // Sem histórico por construção: a projeção só tem id e nome.
        typeof(ClienteMesmoDomicilio).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(nameof(ClienteMesmoDomicilio.ClienteId), nameof(ClienteMesmoDomicilio.Nome));
    }

    private static void Endereco(Cliente cliente, string cep, string numero, string complemento) =>
        cliente.Enderecos.Add(new ClienteEndereco
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Cep = cep,
            Numero = numero,
            Complemento = complemento,
            Logradouro = "Rua das Flores",
            Padrao = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
        });
}
