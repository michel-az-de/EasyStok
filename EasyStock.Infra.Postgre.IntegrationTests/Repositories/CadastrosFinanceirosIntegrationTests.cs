using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public class CadastrosFinanceirosIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private async Task<Guid> SemearAsync()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível.");
        await using var db = fixture.CreateDbContext();
        var id = Guid.NewGuid();
        db.Empresas.Add(new Empresa { Id = id, Nome = "R4 teste", Documento = id.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        db.Set<ContaBancaria>().Add(ContaBancaria.Criar(id, Guid.NewGuid(), "Recebimentos", "pagamento", "Banco de teste", "", "123-4", "R4", true, Guid.NewGuid()));
        var metodo = MetodoPagamentoEmpresa.Criar(id, "pix");
        metodo.Configurar(0, Guid.NewGuid(), false, Guid.NewGuid());
        db.Set<MetodoPagamentoEmpresa>().Add(metodo);
        await db.SaveChangesAsync();
        return id;
    }

    [SkippableFact]
    public async Task Rls_IsolaContasEMetodosMesmoSemFiltroEf()
    {
        var empresa = await SemearAsync();
        await SemearAsync();
        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.ToString()}, false)");
        var contas = await db.Set<ContaBancaria>().IgnoreQueryFilters().ToListAsync();
        contas.Should().ContainSingle().Which.EmpresaId.Should().Be(empresa);
        (await db.Set<MetodoPagamentoEmpresa>().IgnoreQueryFilters().ToListAsync()).Should().ContainSingle().Which.EmpresaId.Should().Be(empresa);
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.empresa_id', '', false)");
        (await db.Set<ContaBancaria>().IgnoreQueryFilters().ToListAsync()).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task DuasConexoes_EditamMesmaRevisao_UmaFalhaSemSobrescrever()
    {
        var empresa = await SemearAsync();
        await using var primeira = fixture.CreateDbContext();
        await using var segunda = fixture.CreateDbContext();
        primeira.SetMobileTenantContext(empresa);
        segunda.SetMobileTenantContext(empresa);
        var a = await primeira.Set<ContaBancaria>().SingleAsync();
        var b = await segunda.Set<ContaBancaria>().SingleAsync();
        a.Atualizar(1, Guid.NewGuid(), "Conferida", a.Tipo, a.Instituicao, a.Agencia, a.Conta, a.Titular, true, Guid.NewGuid());
        b.Atualizar(1, Guid.NewGuid(), "Obsoleta", b.Tipo, b.Instituicao, b.Agencia, b.Conta, b.Titular, false, Guid.NewGuid());
        await primeira.SaveChangesAsync();
        var salvar = () => segunda.SaveChangesAsync();
        await salvar.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresa);
        var persistida = await new CadastrosFinanceirosRepository(leitura).ObterContaAsync(empresa, a.Id, default);
        persistida!.Nome.Should().Be("Conferida");
        persistida.Ativo.Should().BeTrue();
        persistida.Revisao.Should().Be(2);
    }

    [SkippableFact]
    public async Task Relacionamento_FiltraAntesDaPaginacao_EPreservaLeadVinculado()
    {
        var empresa = await SemearAsync();
        var outra = await SemearAsync();
        await using var db = fixture.CreateDbContext();
        db.Clientes.AddRange(new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = "Lead A", OrderCount = 0 },
            new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = "Lead B", Telefone = "(11) 91234-5678", OrderCount = 0, Ativo = true },
            new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = "Cliente", OrderCount = 1 },
            new Cliente { Id = Guid.NewGuid(), EmpresaId = outra, Nome = "Lead estrangeiro", OrderCount = 0 });
        await db.SaveChangesAsync();
        db.SetMobileTenantContext(empresa);
        var repo = new ClienteRepository(db);
        var leads = await repo.GetByEmpresaAsync(empresa, 2, 1, relacionamento: "lead");
        leads.total.Should().Be(2);
        leads.items.Should().ContainSingle().Which.Nome.Should().Be("Lead B");
        (await repo.GetByEmpresaAsync(empresa, 1, 20, search: "11912345678")).items.Should().ContainSingle().Which.Nome.Should().Be("Lead B");
        (await repo.SearchAsync(empresa, "11912345678")).Should().ContainSingle().Which.Nome.Should().Be("Lead B");
        (await repo.GetByEmpresaAsync(empresa, 1, 20, search: "+5511912345678")).items.Should().ContainSingle().Which.Nome.Should().Be("Lead B");
        (await repo.SearchAsync(empresa, "+55 (11) 91234-5678")).Should().ContainSingle().Which.Nome.Should().Be("Lead B");
        var cliente = await db.Clientes.SingleAsync(c => c.Nome == "Lead A");
        db.AtendimentoConversas.Add(EasyStock.Domain.Entities.Atendimento.Conversa.Abrir(empresa, "5511912342395", DateTime.UtcNow, cliente.Nome, cliente.Id));
        await db.SaveChangesAsync();
        (await repo.GetByEmpresaAsync(empresa, 1, 20, relacionamento: "lead")).total.Should().Be(2);
        cliente.OrderCount = 1;
        await db.SaveChangesAsync();
        (await repo.GetByEmpresaAsync(empresa, 1, 20, relacionamento: "lead")).total.Should().Be(1);
        (await repo.GetByEmpresaAsync(empresa, 1, 20, relacionamento: "cliente")).total.Should().Be(2);
    }

    [SkippableFact]
    public async Task BuscaProduto_FiltraSituacaoAntesDeLimitar()
    {
        var empresa = await SemearAsync();
        await using var db = fixture.CreateDbContext();
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa, Nome = "R4", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        db.Categorias.Add(categoria);
        db.Produtos.AddRange(new Produto { Id = Guid.NewGuid(), EmpresaId = empresa, CategoriaId = categoria.Id, Nome = "A teste inativo", Status = EasyStock.Domain.Enums.StatusProduto.Inativo },
            new Produto { Id = Guid.NewGuid(), EmpresaId = empresa, CategoriaId = categoria.Id, Nome = "Z teste ativo", Status = EasyStock.Domain.Enums.StatusProduto.Ativo });
        await db.SaveChangesAsync();
        db.SetMobileTenantContext(empresa);
        var produtos = await new ProdutoRepository(db).SearchAsync(empresa, "teste", 1, EasyStock.Domain.Enums.StatusProduto.Ativo);
        produtos.Should().ContainSingle().Which.Nome.Should().Be("Z teste ativo");
    }
}
