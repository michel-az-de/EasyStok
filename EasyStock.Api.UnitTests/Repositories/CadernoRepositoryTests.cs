using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>S54: o caderno lista só a empresa pedida e, por padrão, só os trechos ativos, em ordem de título.</summary>
public sealed class CadernoRepositoryTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly string _banco = $"caderno-{Guid.NewGuid()}";

    private EasyStockDbContext NovoContexto()
    {
        var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase(_banco)
            .Options);
        db.SetMobileTenantContext(_empresaId);
        return db;
    }

    [Fact]
    public async Task ListaSoAEmpresaESoAtivosEmOrdemDeTitulo()
    {
        var arquivado = TrechoCaderno.Criar(_empresaId, "Antigo", "x", null, false, Agora);
        arquivado.Arquivar(true, Agora);
        using (var db = NovoContexto())
        {
            var repo = new CadernoRepository(db);
            await repo.AddAsync(TrechoCaderno.Criar(_empresaId, "Troca", "x", null, false, Agora));
            await repo.AddAsync(TrechoCaderno.Criar(_empresaId, "Entrega", "x", null, true, Agora));
            await repo.AddAsync(arquivado);
            await repo.AddAsync(TrechoCaderno.Criar(Guid.NewGuid(), "De outra loja", "x", null, false, Agora));
            await db.SaveChangesAsync();
        }

        using (var db = NovoContexto())
        {
            var repo = new CadernoRepository(db);

            (await repo.ListarAsync(_empresaId, incluirArquivados: false)).Select(t => t.Titulo)
                .Should().Equal("Entrega", "Troca");
            (await repo.ListarAsync(_empresaId, incluirArquivados: true)).Should().HaveCount(3);
            (await repo.ObterAsync(_empresaId, arquivado.Id)).Should().NotBeNull();
            (await repo.ObterAsync(Guid.NewGuid(), arquivado.Id)).Should().BeNull();
        }
    }

    public void Dispose()
    {
        using var db = NovoContexto();
        db.Database.EnsureDeleted();
    }
}
