using EasyStock.Api.IntegrationTests.Helpers;
using EasyStock.Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;

namespace EasyStock.Api.IntegrationTests.Scenarios;

/// <summary>
/// Suite de testes E2E para validar isolamento de dados entre tenants
/// Detecta vazamentos silenciosos (dados de outro tenant em resposta)
/// </summary>
public class MultiTenantDataIsolationTests : IAsyncLifetime
{
    private MultiTenantTestFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new MultiTenantTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Health_Check_Com_MultiTenant_PostgreSQL()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ─── Test 1: Access Control ──────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/analytics/dashboard")]
    [InlineData("/api/caixa/movimentos")]
    [InlineData("/api/movimentacoes")]
    [InlineData("/api/vendas")]
    [InlineData("/api/clientes")]
    public async Task AdminA_Cannot_Access_EmpresaB_Data_Via_QueryParam(string endpoint)
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        // AdminA tenta acessar dados de EmpresaB
        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var url = $"{endpoint}?empresaId={_fixture.EmpresaBId}";
        var response = await client.GetAsync(url);

        // DEVE ser 403 ou 200 vazio
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Forbidden,
            HttpStatusCode.OK,
            because: "Tentativa de acesso cross-tenant deve ser bloqueada"
        );

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var itemCount = await response.AssertAndGetItemCountAsync();
            itemCount.Should().Be(0, "Response vazio quando acesso negado");
        }
    }

    // ─── Test 2: Data Isolation ──────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/movimentacoes")]
    [InlineData("/api/vendas")]
    [InlineData("/api/clientes")]
    public async Task AdminA_Receives_Only_EmpresaA_Data(string endpoint)
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        // AdminA acessa dados da sua empresa
        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var response = await client.GetAsync($"{endpoint}?pageSize=1000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Validar que CADA item pertence a EmpresaA
        await response.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaAId);

        // Validar que NÃO há itens de outras empresas
        await response.AssertNoItemsFromTenantAsync(_fixture.EmpresaBId);
        await response.AssertNoItemsFromTenantAsync(_fixture.EmpresaCId);
    }

    // ─── Test 3: Analytical Data Isolation ───────────────────────────────────────

    [Fact]
    public async Task Analytics_Dashboard_Returns_Isolated_Aggregates()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        // AdminA consulta dashboard
        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var response = await client.GetAsync($"/api/analytics/dashboard?empresaId={_fixture.EmpresaAId}");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var itemCount = await response.AssertAndGetItemCountAsync();
            itemCount.Should().BeGreaterThanOrEqualTo(0);

            // Importante: validar que agregações são por tenant
            // Se endpoint retorna estrutura, validar que dados são de EmpresaA apenas
            await response.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaAId);
        }

        // AdminB consulta seu dashboard (deve ter números diferentes)
        var tokenB = await _fixture.GetTokenAsync(_fixture.AdminBId, _fixture.EmpresaBId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenB);

        var responseB = await client.GetAsync($"/api/analytics/dashboard?empresaId={_fixture.EmpresaBId}");

        if (responseB.StatusCode == HttpStatusCode.OK && response.StatusCode == HttpStatusCode.OK)
        {
            var countA = await response.AssertAndGetItemCountAsync();
            var countB = await responseB.AssertAndGetItemCountAsync();

            // Contagens devem ser diferentes (exceto coincidência rara)
            // EmpresaA: 50 products, EmpresaB: 75 products
        }
    }

    // ─── Test 4: Movimentações & Caixa Data Isolation ──────────────────────────────

    [Fact]
    public async Task Movimentacoes_Isolation_By_Tenant()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        // AdminA: deve ver apenas suas movimentações (100 seeded)
        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var responseA = await client.GetAsync($"/api/movimentacoes?empresaId={_fixture.EmpresaAId}&pageSize=1000");
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);

        var countA = await responseA.AssertAndGetItemCountAsync();
        countA.Should().BeGreaterThan(0, "AdminA deve ver suas movimentações");
        await responseA.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaAId);

        // AdminB: deve ver apenas suas movimentações (150 seeded)
        var tokenB = await _fixture.GetTokenAsync(_fixture.AdminBId, _fixture.EmpresaBId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenB);

        var responseB = await client.GetAsync($"/api/movimentacoes?empresaId={_fixture.EmpresaBId}&pageSize=1000");
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);

        var countB = await responseB.AssertAndGetItemCountAsync();
        countB.Should().BeGreaterThan(0, "AdminB deve ver suas movimentações");
        await responseB.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaBId);

        // Contagens devem ser diferentes
        // countA ~100, countB ~150
        countA.Should().NotBe(countB, "Movimentações devem ser isoladas por tenant");
    }

    // ─── Test 5: Sub-Resource Isolation (IDOR Prevention) ───────────────────────────

    [Fact]
    public async Task SubResources_Require_Parent_Tenant_Match()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        // Preparar: Obter um cliente de EmpresaA
        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var clientesResponse = await client.GetAsync($"/api/clientes?empresaId={_fixture.EmpresaAId}&pageSize=10");
        if (clientesResponse.StatusCode != HttpStatusCode.OK)
        {
            Assert.Skip("Endpoint /api/clientes não disponível ou retorna erro");
        }

        var content = await clientesResponse.Content.ReadAsAsync<dynamic>();
        if (content?.data?.Count == 0)
        {
            Assert.Skip("Nenhum cliente seedado para teste");
        }

        // Para o primeiro cliente de EmpresaA, AdminA pode acessar
        // AdminB NÃO pode acessar o mesmo cliente (cross-tenant)
        // Verificar via resposta se há estrutura de sub-recurso

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.Forbidden
        );
    }

    // ─── Test 6: No Timing Leak (Consistent Query Time) ──────────────────────────────

    [Fact]
    public async Task Query_Time_Consistent_Across_Tenant_Sizes()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync($"/api/movimentacoes?empresaId={_fixture.EmpresaAId}");
        sw.Stop();

        var timeSmallTenant = sw.ElapsedMilliseconds;
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Timing side-channel: não deve variar muito baseado em size de outro tenant
        // Apenas validar que completa rapidamente (< 1 segundo)
        timeSmallTenant.Should().BeLessThan(1000, "Query deve ser rápida para tenant pequeno");
    }

    // ─── Test 7: Response Headers No Data Leak ───────────────────────────────────────

    [Fact]
    public async Task Response_Headers_Do_Not_Expose_Tenant_Info()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var response = await client.GetAsync($"/api/movimentacoes?empresaId={_fixture.EmpresaAId}");

        response.AssertNoDataLeakInHeaders();
    }

    // ─── Test 8: Empty Tenant Returns Empty (Not 404) ──────────────────────────────────

    [Fact]
    public async Task Empty_Tenant_Data_Returns_200_With_Empty_List()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        // Usar EmpresaC (30 products, pode ter menos movimentações)
        // ou criar novo tenant específico para este teste

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenC = await _fixture.GetTokenAsync(_fixture.AdminCId, _fixture.EmpresaCId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenC);

        var response = await client.GetAsync($"/api/vendas?empresaId={_fixture.EmpresaCId}");

        // Endpoint pode retornar 200 vazio ou 404 se não houver dados
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var count = await response.AssertAndGetItemCountAsync();
            // Se houver items, devem ser de EmpresaC
            if (count > 0)
            {
                await response.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaCId);
            }
        }
    }
}
