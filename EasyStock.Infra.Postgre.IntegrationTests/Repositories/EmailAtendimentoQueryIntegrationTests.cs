using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #3101: <c>ObterClienteIdPorEmailAsync</c> compara a coluna gerada <c>email_lower</c> (índice
/// <c>ix_clientes_empresa_email_lower</c>). Sob RLS o Postgres só usa como Index Cond o que é leakproof, e
/// <c>lower()</c> não é: um índice de expressão não serviria, por isso a coluna gerada.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class EmailAtendimentoQueryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private async Task<Guid> SemearEmpresaAsync()
    {
        var empresa = Empresa.Criar("Empresa Email " + Guid.NewGuid().ToString("N")[..6], null);
        await using var db = fixture.CreateDbContext();
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        return empresa.Id;
    }

    private async Task<Guid> SemearClienteAsync(Guid empresaId, string? email, bool ativo = true, DateTime? criadoEm = null)
    {
        var id = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Clientes.Add(new Cliente
        {
            Id = id, EmpresaId = empresaId, Nome = "Cliente " + id.ToString("N")[..6], Email = email,
            Ativo = ativo, OrderCount = 0, CriadoEm = criadoEm ?? DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid?> ObterAsync(Guid empresaId, string email)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return await new EmailAtendimentoQuery(db).ObterClienteIdPorEmailAsync(empresaId, email);
    }

    [SkippableTheory]
    [InlineData("ana@loja.com")]
    [InlineData("ANA@Loja.COM")]
    [InlineData("  Ana@loja.com ")]
    public async Task Acha_o_cliente_sem_diferenciar_caixa_nem_espacos_nas_pontas(string busca)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await SemearEmpresaAsync();
        var id = await SemearClienteAsync(empresa, "Ana@Loja.com");

        (await ObterAsync(empresa, busca)).Should().Be(id);
    }

    [SkippableFact]
    public async Task Ignora_cliente_inativo_e_cliente_de_outra_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await SemearEmpresaAsync();
        var outra = await SemearEmpresaAsync();
        await SemearClienteAsync(empresa, "inativo@loja.com", ativo: false);
        await SemearClienteAsync(outra, "alheio@loja.com");

        (await ObterAsync(empresa, "inativo@loja.com")).Should().BeNull();
        (await ObterAsync(empresa, "alheio@loja.com")).Should().BeNull("o cliente é de outra empresa");
    }

    [SkippableFact]
    public async Task Com_dois_clientes_de_mesmo_email_vence_o_mais_antigo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await SemearEmpresaAsync();
        var agora = DateTime.UtcNow;
        await SemearClienteAsync(empresa, "dup@loja.com", criadoEm: agora);
        var antigo = await SemearClienteAsync(empresa, "DUP@loja.com", criadoEm: agora.AddDays(-3));

        (await ObterAsync(empresa, "dup@loja.com")).Should().Be(antigo);
    }

    [SkippableFact]
    public async Task Email_nulo_nao_casa_e_texto_vazio_devolve_null()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await SemearEmpresaAsync();
        await SemearClienteAsync(empresa, null);

        (await ObterAsync(empresa, "qualquer@loja.com")).Should().BeNull();
        (await ObterAsync(empresa, "   ")).Should().BeNull();
    }

    [SkippableFact]
    public async Task Consulta_compara_a_coluna_gerada_email_lower()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await SemearEmpresaAsync();
        var id = await SemearClienteAsync(empresa, "sql@loja.com");
        var comandos = new List<string>();

        await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .LogTo(comandos.Add, [RelationalEventId.CommandExecuted])
            .Options);
        db.SetMobileTenantContext(empresa);

        (await new EmailAtendimentoQuery(db).ObterClienteIdPorEmailAsync(empresa, "SQL@loja.com")).Should().Be(id);

        comandos.Should().Contain(sql => sql.Contains("email_lower"));
    }

    // Prova do índice sob RLS: login comum (NOSUPERUSER, NOBYPASSRLS, não-dono) com o tenant ligado.
    // seqscan desligado porque a tabela do teste é minúscula; o que se mede é se o índice PODE servir.
    [SkippableFact]
    public async Task Sob_RLS_o_plano_usa_o_indice_na_coluna_gerada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await SemearEmpresaAsync();
        await SemearClienteAsync(empresa, "plano@loja.com");

        await using var conexao = new NpgsqlConnection(fixture.RlsClientConnectionString);
        await conexao.OpenAsync();

        async Task<object?> Escalar(string sql, params NpgsqlParameter[] ps)
        {
            await using var cmd = new NpgsqlCommand(sql, conexao);
            cmd.Parameters.AddRange(ps);
            return await cmd.ExecuteScalarAsync();
        }

        (await Escalar("""
            SELECT (r.rolsuper OR r.rolbypassrls OR c.relowner = r.oid)
            FROM pg_roles r CROSS JOIN pg_class c WHERE r.rolname = current_user AND c.relname = 'clientes'
            """)).Should().Be(false, "a prova usa login NOSUPERUSER/NOBYPASSRLS e não-dono");
        await Escalar("SELECT set_config('app.empresa_id', @e, false)", new NpgsqlParameter("e", empresa.ToString()));
        await Escalar("SET enable_seqscan = off");

        var plano = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            $"EXPLAIN SELECT \"Id\" FROM clientes WHERE \"EmpresaId\" = '{empresa}' AND email_lower = 'x@y.com'", conexao))
        await using (var leitor = await cmd.ExecuteReaderAsync())
            while (await leitor.ReadAsync())
                plano.Add(leitor.GetString(0));

        var texto = string.Join('\n', plano);
        texto.Should().Contain("ix_clientes_empresa_email_lower");
        texto.Should().MatchRegex(@"Index Cond:.*email_lower");
    }
}
