using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// #1311: a tag nova entra pela coleção do cliente já rastreado (<c>registrar_restricao</c> do agente).
/// Com o Id gerado no app, o EF precisa inserir a tag; antes ele tentava um UPDATE de 0 linhas e o turno
/// inteiro do agente falhava com <see cref="DbUpdateConcurrencyException"/>.
/// </summary>
public sealed class ClienteCrmRepositoryTagTests : IDisposable
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly string _banco = $"cliente-tag-{Guid.NewGuid()}";

    private EasyStockDbContext NovoContexto()
    {
        var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase(_banco)
            .Options);
        db.SetMobileTenantContext(_empresaId);
        return db;
    }

    [Fact]
    public async Task TagNovaEmClienteRastreadoEhGravada()
    {
        var cliente = Cliente.Criar(_empresaId, "Mariana");
        using (var db = NovoContexto())
        {
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
        }

        using (var db = NovoContexto())
        {
            var rastreado = await new ClienteCrmRepository(db).ObterComTagsAsync(_empresaId, cliente.Id);
            rastreado!.AdicionarTag("alergia castanha", OrigemClienteTag.Agente, DateTime.UtcNow);

            var gravar = () => db.SaveChangesAsync();

            await gravar.Should().NotThrowAsync();
        }

        using (var db = NovoContexto())
        {
            var tags = await db.Set<ClienteTag>().Where(t => t.ClienteId == cliente.Id).ToListAsync();
            tags.Should().ContainSingle().Which.Origem.Should().Be(OrigemClienteTag.Agente);
        }
    }

    public void Dispose()
    {
        using var db = NovoContexto();
        db.Database.EnsureDeleted();
    }
}
