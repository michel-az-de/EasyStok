using System.Globalization;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// #2581: garante a migration <c>RlsTabelasFilhasBabaRo</c> (RLS tenant_isolation nas 17 tabelas
/// filhas cuja empresa vem do pai). Cobertura no catalogo do Postgres e isolamento por SQL cru
/// (sem query filters do EF) em pedido_itens, cliente_telefones e cardapio_item_variacao.
/// </summary>
public class RlsTabelasFilhasTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly string[] Filhas =
    [
        "lote_itens", "pedidos_fornecedor_itens", "itens_lista_compras", "pedido_itens",
        "pedido_pagamentos", "pedido_eventos", "itens_venda", "cliente_telefones",
        "cliente_enderecos", "cardapio_item", "cardapio_item_variacao", "cardapio_secao",
        "frete_zona", "janela_entrega", "viagem_paradas", "nfe_itens", "fatura_itens",
    ];

    private static readonly string[] TabelasDoIsolamento =
        ["pedido_itens", "cliente_telefones", "cardapio_item_variacao"];

    [SkippableFact]
    public async Task As_17_tabelas_filhas_tem_RLS_forcada_e_policy_tenant_isolation()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        Filhas.Should().HaveCount(17);

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();

        var comRls = await ctx.Database.SqlQueryRaw<string>(
            @"SELECT c.relname AS ""Value"" FROM pg_class c
              JOIN pg_namespace n ON n.oid = c.relnamespace
              WHERE n.nspname = 'public' AND c.relkind = 'r'
                AND c.relrowsecurity AND c.relforcerowsecurity").ToListAsync();
        var comPolicy = await ctx.Database.SqlQueryRaw<string>(
            @"SELECT tablename AS ""Value"" FROM pg_policies
              WHERE schemaname = 'public' AND policyname = 'tenant_isolation'").ToListAsync();

        comRls.Should().Contain(Filhas, "relrowsecurity e relforcerowsecurity ligados nas 17 filhas");
        comPolicy.Should().Contain(Filhas, "policy tenant_isolation criada nas 17 filhas");
    }

    [SkippableFact]
    public async Task Sem_set_de_tenant_as_filhas_retornam_zero_linhas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await SeedDuasEmpresasAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();

        foreach (var tabela in TabelasDoIsolamento)
        {
            (await ContarLinhasAsync(ctx, tabela)).Should().Be(0, $"{tabela}: fail-closed sem tenant");
        }
    }

    [SkippableFact]
    public async Task Tenant_A_ve_so_as_filhas_de_A()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var seed = await SeedDuasEmpresasAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantManualAsync(ctx, seed.EmpresaA);

        foreach (var tabela in TabelasDoIsolamento)
        {
            (await ContarLinhasAsync(ctx, tabela)).Should().Be(1, $"{tabela}: tenant A ve so a sua linha");
        }
        (await ContarPorIdAsync(ctx, "pedido_itens", seed.PedidoItemA)).Should().Be(1);
        (await ContarPorIdAsync(ctx, "pedido_itens", seed.PedidoItemB)).Should().Be(0);
        (await ContarPorIdAsync(ctx, "cliente_telefones", seed.TelefoneB)).Should().Be(0);
        (await ContarPorIdAsync(ctx, "cardapio_item_variacao", seed.VariacaoB)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Tenant_B_ve_so_as_filhas_de_B()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var seed = await SeedDuasEmpresasAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantManualAsync(ctx, seed.EmpresaB);

        foreach (var tabela in TabelasDoIsolamento)
        {
            (await ContarLinhasAsync(ctx, tabela)).Should().Be(1, $"{tabela}: tenant B ve so a sua linha");
        }
        (await ContarPorIdAsync(ctx, "pedido_itens", seed.PedidoItemB)).Should().Be(1);
        (await ContarPorIdAsync(ctx, "pedido_itens", seed.PedidoItemA)).Should().Be(0);
        (await ContarPorIdAsync(ctx, "cliente_telefones", seed.TelefoneA)).Should().Be(0);
        (await ContarPorIdAsync(ctx, "cardapio_item_variacao", seed.VariacaoA)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Bypass_RLS_ve_as_filhas_dos_dois_tenants()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await SeedDuasEmpresasAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        foreach (var tabela in TabelasDoIsolamento)
        {
            (await ContarLinhasAsync(ctx, tabela)).Should().Be(2, $"{tabela}: bypass ve A e B");
        }
    }

    [SkippableFact]
    public async Task Insert_em_pedido_itens_apontando_para_pedido_de_outra_empresa_e_barrado_pelo_WITH_CHECK()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var seed = await SeedDuasEmpresasAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await SetTenantManualAsync(ctx, seed.EmpresaA);

        // Sessao de A tenta anexar um item ao pedido de B: o EXISTS da policy nao acha o pai
        // visivel para A, entao o WITH CHECK recusa (42501).
        var inserirNoPedidoDeB = async () => await InserirAsync(ctx, "pedido_itens",
            ("Id", Guid.NewGuid()), ("PedidoId", seed.PedidoB), ("Nome", "Hack"));

        await inserirNoPedidoDeB.Should()
            .ThrowAsync<Npgsql.PostgresException>()
            .Where(ex => ex.SqlState == "42501", "WITH CHECK deve recusar item em pedido de outro tenant");

        // Controle positivo: o mesmo INSERT no pedido de A passa.
        await InserirAsync(ctx, "pedido_itens",
            ("Id", Guid.NewGuid()), ("PedidoId", seed.PedidoA), ("Nome", "Legitimo"));
    }

    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Seed(
        Guid EmpresaA, Guid EmpresaB,
        Guid PedidoA, Guid PedidoB,
        Guid PedidoItemA, Guid PedidoItemB,
        Guid TelefoneA, Guid TelefoneB,
        Guid VariacaoA, Guid VariacaoB);

    private async Task<Seed> SeedDuasEmpresasAsync()
    {
        await fixture.ResetDatabaseAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        // Conexao aberta: o SET vive pela sessao Npgsql (ver RowLevelSecurityTests).
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        var (empresaA, pedidoA, itemA, telA, varA) = await SeedEmpresaAsync(ctx, "A", "11111111111");
        var (empresaB, pedidoB, itemB, telB, varB) = await SeedEmpresaAsync(ctx, "B", "22222222222");
        return new Seed(empresaA, empresaB, pedidoA, pedidoB, itemA, itemB, telA, telB, varA, varB);
    }

    private static async Task<(Guid Empresa, Guid Pedido, Guid PedidoItem, Guid Telefone, Guid Variacao)>
        SeedEmpresaAsync(EasyStockDbContext ctx, string sufixo, string documento)
    {
        var empresa = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var pedidoItem = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        var telefone = Guid.NewGuid();
        var storefront = Guid.NewGuid();
        var cardapioItem = Guid.NewGuid();
        var variacao = Guid.NewGuid();

        await InserirAsync(ctx, "empresas", ("Id", empresa), ("Nome", $"Empresa {sufixo}"), ("Documento", documento));
        await InserirAsync(ctx, "clientes", ("Id", cliente), ("EmpresaId", empresa), ("Nome", $"Cliente {sufixo}"));
        await InserirAsync(ctx, "cliente_telefones", ("Id", telefone), ("ClienteId", cliente), ("Numero", $"1199999000{sufixo.Length}"));
        await InserirAsync(ctx, "pedidos", ("Id", pedido), ("EmpresaId", empresa), ("Status", "aguardando"));
        await InserirAsync(ctx, "pedido_itens", ("Id", pedidoItem), ("PedidoId", pedido), ("Nome", $"Item {sufixo}"));
        await InserirAsync(ctx, "storefront", ("Id", storefront), ("EmpresaId", empresa),
            ("Slug", $"loja-{sufixo.ToLowerInvariant()}"), ("TituloPublico", $"Loja {sufixo}"));
        await InserirAsync(ctx, "cardapio_item", ("Id", cardapioItem), ("StorefrontId", storefront),
            ("NomePublico", $"Prato {sufixo}"));
        await InserirAsync(ctx, "cardapio_item_variacao",
            ("Id", variacao), ("CardapioItemId", cardapioItem), ("Rotulo", $"Var {sufixo}"));

        return (empresa, pedido, pedidoItem, telefone, variacao);
    }

    /// <summary>
    /// INSERT por SQL cru: usa as colunas informadas e completa as demais NOT NULL sem default
    /// com um valor neutro pelo tipo, para nao acoplar o teste ao shape de cada entidade.
    /// </summary>
    private static async Task InserirAsync(
        EasyStockDbContext ctx, string tabela, params (string Coluna, object Valor)[] valores)
    {
        var informadas = valores.Select(v => v.Coluna).ToHashSet(StringComparer.Ordinal);
        var obrigatorias = await ctx.Database.SqlQueryRaw<ColunaObrigatoria>(
            @"SELECT column_name AS ""Nome"", data_type AS ""Tipo"" FROM information_schema.columns
              WHERE table_schema = 'public' AND table_name = {0}
                AND is_nullable = 'NO' AND column_default IS NULL
                AND is_identity = 'NO' AND is_generated = 'NEVER'", tabela).ToListAsync();

        var colunas = new List<string>();
        var literais = new List<string>();
        foreach (var (coluna, valor) in valores)
        {
            colunas.Add($"\"{coluna}\"");
            literais.Add(Literal(valor));
        }
        foreach (var c in obrigatorias.Where(c => !informadas.Contains(c.Nome)))
        {
            colunas.Add($"\"{c.Nome}\"");
            literais.Add(PadraoPorTipo(c.Tipo));
        }

        // EF1002 suprimido: valores sao Guids/literais gerados pelo teste (injecao impossivel).
#pragma warning disable EF1002
        await ctx.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {tabela} ({string.Join(", ", colunas)}) VALUES ({string.Join(", ", literais)})");
#pragma warning restore EF1002
    }

    private static string Literal(object valor) => valor switch
    {
        Guid g => $"'{g}'::uuid",
        string s => $"'{s.Replace("'", "''", StringComparison.Ordinal)}'",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException(valor.GetType().Name),
    };

    private static string PadraoPorTipo(string tipo) => tipo switch
    {
        "uuid" => "gen_random_uuid()",
        "boolean" => "false",
        "integer" or "bigint" or "smallint" or "numeric" or "double precision" or "real" => "0",
        "timestamp with time zone" or "timestamp without time zone" => "now()",
        "date" => "current_date",
        "jsonb" => "'[]'::jsonb",
        "json" => "'[]'::json",
        "bytea" => "'\\x00'::bytea",
        _ => "'x'",
    };

    private static async Task SetTenantManualAsync(EasyStockDbContext ctx, Guid tenantId)
    {
        // Simula o interceptor, isolando a camada do banco (mesmo padrao de RowLevelSecurityTests).
#pragma warning disable EF1002
        await ctx.Database.ExecuteSqlRawAsync(
            $"SET app.empresa_id = '{tenantId}'; SET app.bypass_rls = 'false';");
#pragma warning restore EF1002
    }

    private static async Task<int> ContarLinhasAsync(EasyStockDbContext ctx, string tabela)
    {
        var sql = $"SELECT COUNT(*)::int AS \"Value\" FROM {tabela}";
        return await ctx.Database.SqlQueryRaw<int>(sql).FirstAsync();
    }

    private static async Task<int> ContarPorIdAsync(EasyStockDbContext ctx, string tabela, Guid id)
    {
        var sql = $"SELECT COUNT(*)::int AS \"Value\" FROM {tabela} WHERE \"Id\" = '{id}'";
        return await ctx.Database.SqlQueryRaw<int>(sql).FirstAsync();
    }

    private sealed record ColunaObrigatoria(string Nome, string Tipo);
}
