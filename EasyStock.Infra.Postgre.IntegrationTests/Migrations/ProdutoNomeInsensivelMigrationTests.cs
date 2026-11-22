using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

/// <summary>
/// #3101: a unicidade de nome de produto por empresa passa a ignorar caixa no banco, pelo índice único de
/// expressão <c>uq_produtos_empresa_nome_lower</c>. A migração sobe mesmo com colisão prévia: mantém o
/// produto mais antigo e renomeia os demais, sem apagar nada. Roda em PostgreSQL de verdade.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public class ProdutoNomeInsensivelMigrationTests(PostgreSqlDatabaseFixture fixture)
{
    private const string Migracao = "AddUniqueProdutoNomeInsensivelPorEmpresa";

    [SkippableFact]
    public async Task SobeComColisaoPrevia_RenomeiaOMaisNovo_EPassaARecusarNomeQueSoMudaNaCaixa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var antigoId = Guid.NewGuid();
        var novoId = Guid.NewGuid();
        var outraEmpresaId = Guid.NewGuid();
        var outraCategoriaId = Guid.NewGuid();
        var daOutraEmpresaId = Guid.NewGuid();
        var antes = DateTime.UtcNow.AddDays(-2);

        await using (var db = fixture.CreateDbContext())
        {
            var migracoes = db.Database.GetMigrations().ToList();
            var alvo = migracoes.Single(m => m.EndsWith("_" + Migracao, StringComparison.Ordinal));
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(migracoes[migracoes.IndexOf(alvo) - 1]);

            // Banco de antes: o índice antigo diferencia caixa, então os dois nomes coexistem.
            db.SetMobileTenantContext(empresaId);
            await InserirEmpresaAsync(db, empresaId, "Empresa Colisao", antes);
            db.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Limpeza", CriadoEm = antes, AlteradoEm = antes });
            db.Produtos.Add(NovoProduto(antigoId, empresaId, categoriaId, "Vassoura", antes));
            db.Produtos.Add(NovoProduto(novoId, empresaId, categoriaId, "vassoura", antes.AddDays(1)));
            await db.SaveChangesAsync();

            // Outra empresa com o mesmo nome: a unicidade e o dedup sao por empresa.
            db.ChangeTracker.Clear();
            db.SetMobileTenantContext(outraEmpresaId);
            await InserirEmpresaAsync(db, outraEmpresaId, "Outra Empresa", antes);
            db.Categorias.Add(new Categoria { Id = outraCategoriaId, EmpresaId = outraEmpresaId, Nome = "Limpeza", CriadoEm = antes, AlteradoEm = antes });
            db.Produtos.Add(NovoProduto(daOutraEmpresaId, outraEmpresaId, outraCategoriaId, "VASSOURA", antes.AddDays(1)));
            await db.SaveChangesAsync();

            await migrator.MigrateAsync(alvo);

            // A RLS sai da migracao como entrou: ligada e forcada.
            (await db.Database.SqlQuery<bool>($"""
                SELECT (relrowsecurity AND relforcerowsecurity) AS "Value" FROM pg_class WHERE oid = 'produtos'::regclass
                """).SingleAsync()).Should().BeTrue();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var produtos = await db.Produtos.AsNoTracking().Where(p => p.EmpresaId == empresaId)
                .ToDictionaryAsync(p => p.Id);

            produtos.Should().HaveCount(2, "a migração renomeia, não apaga");
            produtos[antigoId].Nome.Should().Be("Vassoura", "o mais antigo mantém o nome");
            produtos[antigoId].AlteradoEm.Should().BeBefore(antes.AddHours(1), "quem não foi renomeado não muda");
            produtos[novoId].Nome.Should().Be($"vassoura (dup {novoId.ToString()[..8]})");
            produtos[novoId].AlteradoEm.Should().BeAfter(antes.AddDays(1).AddHours(1), "o pull do mobile só leva produto com AlteradoEm novo");

            db.Produtos.Add(NovoProduto(Guid.NewGuid(), empresaId, categoriaId, "VASSOURA", DateTime.UtcNow));
            var gravar = () => db.SaveChangesAsync();

            (await gravar.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<PostgresException>()
                .Which.ConstraintName.Should().Be("uq_produtos_empresa_nome_lower");
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(outraEmpresaId);
            var daOutra = await db.Produtos.AsNoTracking().SingleAsync(p => p.Id == daOutraEmpresaId);
            daOutra.Nome.Should().Be("VASSOURA", "o mesmo nome em outra empresa não é duplicado");
            daOutra.AlteradoEm.Should().BeBefore(DateTime.UtcNow.AddHours(-1));
        }

        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.MigrateAsync();
            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
    }

    // Revisão da #3135: o nome que sobra é o do produto ATIVO, mesmo sendo o mais novo. Renomear o ativo
    // poria "(dup ...)" no cardápio e no PDV, e o Down não restaura.
    [SkippableFact]
    public async Task ComTresDuplicatas_OAtivoMantemONome_MesmoSendoMaisNovoQueOInativo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var inativoAntigoId = Guid.NewGuid();
        var ativoId = Guid.NewGuid();
        var ativoNovoId = Guid.NewGuid();
        var antes = DateTime.UtcNow.AddDays(-5);
        var longo = new string('n', 175);
        var longoAntigoId = Guid.NewGuid();
        var longoNovoId = Guid.NewGuid();

        await using (var db = fixture.CreateDbContext())
        {
            var migracoes = db.Database.GetMigrations().ToList();
            var alvo = migracoes.Single(m => m.EndsWith("_" + Migracao, StringComparison.Ordinal));
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(migracoes[migracoes.IndexOf(alvo) - 1]);

            db.SetMobileTenantContext(empresaId);
            await InserirEmpresaAsync(db, empresaId, "Empresa Tres", antes);
            db.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Doces", CriadoEm = antes, AlteradoEm = antes });
            db.Produtos.Add(NovoProduto(inativoAntigoId, empresaId, categoriaId, "Bolo", antes, StatusProduto.Inativo));
            db.Produtos.Add(NovoProduto(ativoId, empresaId, categoriaId, "bolo", antes.AddDays(1)));
            db.Produtos.Add(NovoProduto(ativoNovoId, empresaId, categoriaId, "BOLO", antes.AddDays(2)));
            db.Produtos.Add(NovoProduto(longoAntigoId, empresaId, categoriaId, longo, antes));
            db.Produtos.Add(NovoProduto(longoNovoId, empresaId, categoriaId, longo.ToUpperInvariant(), antes.AddDays(1)));
            await db.SaveChangesAsync();

            await migrator.MigrateAsync(alvo);
        }

        await using (var leitura = fixture.CreateDbContext())
        {
            leitura.SetMobileTenantContext(empresaId);
            var nomes = await leitura.Produtos.AsNoTracking().Where(p => p.EmpresaId == empresaId)
                .ToDictionaryAsync(p => p.Id, p => p.Nome);

            nomes.Should().HaveCount(5);
            nomes[ativoId].Should().Be("bolo", "entre os ativos, o mais antigo mantém o nome");
            nomes[inativoAntigoId].Should().Be($"Bolo (dup {inativoAntigoId.ToString()[..8]})", "o inativo cede o nome ao ativo");
            nomes[ativoNovoId].Should().Be($"BOLO (dup {ativoNovoId.ToString()[..8]})");
            nomes[longoAntigoId].Should().Be(longo);
            nomes[longoNovoId].Should().Be($"{new string('N', 160)} (dup {longoNovoId.ToString()[..8]})", "o nome é cortado para caber em 180");
            await leitura.Database.MigrateAsync();
        }
    }

    /// <summary>
    /// A empresa entra por SQL com as colunas do esquema antigo: o modelo atual tem colunas mais novas (#3179:
    /// configuração inicial em <c>empresas</c>) que o banco descido até antes da migração ainda não tem.
    /// </summary>
    private static Task InserirEmpresaAsync(DbContext db, Guid id, string nome, DateTime em)
    {
        var documento = id.ToString("N")[..14];
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO empresas ("Id", "Nome", "Documento", "CriadoEm", "AlteradoEm", "FinanceiroHabilitado", "OnboardingCompleto")
            VALUES ({id}, {nome}, {documento}, {em}, {em}, false, false)
            """);
    }

    private static Produto NovoProduto(Guid id, Guid empresaId, Guid categoriaId, string nome, DateTime criadoEm, StatusProduto status = StatusProduto.Ativo) => new()
    {
        Id = id, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = nome,
        Tipo = TipoProduto.Fisico, Status = status, CriadoEm = criadoEm, AlteradoEm = criadoEm
    };
}
