using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Repositories;

public sealed class ClienteCrmNotasRepositoryTests
{
    [Fact]
    public async Task PaginasComEmpateDeDataNaoPerdemNotasEAConsultaPorIdRespeitaClienteEEmpresa()
    {
        var empresa = Guid.NewGuid();
        var cliente = Cliente.Criar(empresa, "Cliente das notas");
        var outro = Cliente.Criar(empresa, "Outro cliente");
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"notas-{Guid.NewGuid()}").Options;
        await using var db = new EasyStockDbContext(options);
        db.SetMobileTenantContext(empresa);
        db.Clientes.AddRange(cliente, outro);
        var em = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var notas = Enumerable.Range(0, 151).Select(i => ClienteNota.Criar(empresa, cliente.Id, $"Nota {i}", "Operador", em)).ToArray();
        db.ClienteNotas.AddRange(notas);
        var alheia = ClienteNota.Criar(empresa, outro.Id, "Outro cliente", "Operador", em);
        db.ClienteNotas.Add(alheia);
        await db.SaveChangesAsync();
        var repo = new ClienteCrmRepository(db);
        var recebidas = new List<Guid>();
        for (var pagina = 0; pagina < 4; pagina++)
            recebidas.AddRange((await repo.ListarPaginaNotasAsync(empresa, cliente.Id, pagina * 50, 50)).Select(n => n.Id));
        recebidas.Should().HaveCount(151).And.OnlyHaveUniqueItems();
        recebidas.Should().Equal(notas.OrderByDescending(n => n.Id).Select(n => n.Id));
        (await repo.ObterNotaAsync(empresa, cliente.Id, notas[0].Id))!.Id.Should().Be(notas[0].Id);
        (await repo.ObterNotaAsync(empresa, outro.Id, notas[0].Id)).Should().BeNull();
        (await repo.ObterNotaAsync(Guid.NewGuid(), cliente.Id, notas[0].Id)).Should().BeNull();
        (await repo.ListarPaginaNotasAsync(Guid.NewGuid(), cliente.Id, 0, 51)).Should().BeEmpty();
    }
}
