using EasyStock.Api.IntegrationTests.Helpers;
using EasyStock.Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using System.Net;
using System.Web;

namespace EasyStock.Api.IntegrationTests.Scenarios;

/// <summary>
/// Testes de bypass e tentativas de exploração do isolamento multi-tenant
/// </summary>
public class QueryBypassAndSecurityTests : IAsyncLifetime
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
    public async Task Query_Parameter_Override_Should_Be_Blocked()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        // AdminA tenta usar query parameter para acessar EmpresaB
        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Tentativa 1: Query parameter simples
        var response = await client.GetAsync(
            $"/api/analytics/dashboard?empresaId={_fixture.EmpresaBId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Query parameter com empresaId diferente deve ser bloqueado pelo ValidateEmpresaIdAttribute");
    }

    [Fact]
    public async Task Multiple_Query_Parameters_Same_Name_Should_Use_First()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Tentativa: ?empresaId={A}&empresaId={B}
        var url = new UriBuilder("http://localhost/api/movimentacoes")
        {
            Query = new QueryString($"?empresaId={_fixture.EmpresaAId}&empresaId={_fixture.EmpresaBId}").Value
        }.Uri.PathAndQuery;

        var response = await client.GetAsync(url);

        // Deve usar o primeiro valor (EmpresaA) ou rejeitar como inválido
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            because: "Múltiplos parâmetros com mesmo nome devem ser tratados"
        );
    }

    [Fact]
    public async Task Header_Injection_Should_Not_Affect_Isolation()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Tentar injetar empresaId em header customizado
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/movimentacoes?pageSize=10");
        request.Headers.Authorization = new("Bearer", tokenA);
        request.Headers.Add("X-Empresa-Id", _fixture.EmpresaBId.ToString());
        request.Headers.Add("X-Tenant-Id", _fixture.EmpresaBId.ToString());

        var response = await client.SendAsync(request);

        // Header customizado NÃO deve override empresaId do JWT
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await response.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaAId,
            "Headers customizados não devem override isolamento JWT");
    }

    [Fact]
    public async Task URL_Encoded_Query_Parameter_Should_Still_Be_Blocked()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Tentar com URL encoding
        var guidBEncoded = HttpUtility.UrlEncode(_fixture.EmpresaBId.ToString());
        var response = await client.GetAsync($"/api/movimentacoes?empresaId={guidBEncoded}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Case_Variation_Query_Parameter_Should_Be_Ignored()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Tentar com case variation: EmpresaId, empresaid, EMPRESAID
        var responses = new List<HttpResponseMessage>();

        var response1 = await client.GetAsync(
            $"/api/movimentacoes?EmpresaId={_fixture.EmpresaBId}");
        responses.Add(response1);

        var response2 = await client.GetAsync(
            $"/api/movimentacoes?empresaid={_fixture.EmpresaBId}");
        responses.Add(response2);

        // Pelo menos um deve ser bloqueado ou ignorado (não usar EmpresaB)
        var allBlocked = responses.All(r => r.StatusCode == HttpStatusCode.Forbidden || r.StatusCode == HttpStatusCode.BadRequest);
        var allValid = responses.All(r => r.StatusCode == HttpStatusCode.OK);

        (allBlocked || allValid).Should().BeTrue("Case variation deve ser tratada consistentemente");

        // Se válido, deve conter apenas dados de EmpresaA
        foreach (var resp in responses.Where(r => r.StatusCode == HttpStatusCode.OK))
        {
            await resp.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaAId);
        }
    }

    [Fact]
    public async Task Sub_Resource_Access_Requires_Parent_Tenant_Match()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        // Este teste assume que há endpoints sub-resource como:
        // GET /api/clientes/{clienteId}/enderecos
        // onde clienteId pertence a EmpresaA e AdminB tenta acessar

        // Skip se não há dados de teste para sub-resources
        Assert.Skip("Sub-resource test requer dados pre-configurados");
    }

    [Fact]
    public async Task CORS_Headers_Should_Not_Leak_Tenant_Info()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Fazer request com header Origin
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/movimentacoes");
        request.Headers.Authorization = new("Bearer", tokenA);
        request.Headers.Add("Origin", "https://suspicious-origin.com");

        var response = await client.SendAsync(request);

        // Se CORS header retorna, não deve conter tenant info
        if (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins))
        {
            var origin = string.Join(",", origins);
            origin.Should().NotContain("empresaId", "CORS header não deve expor empresaId");
        }
    }

    [Fact]
    public async Task Redirect_Response_Should_Not_Expose_Tenant_In_Location()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Qualquer endpoint que redireciona
        var response = await client.GetAsync("/api/auth/refresh-token", new HttpCompletionOption.ResponseHeadersRead);

        response.AssertNoDataLeakInHeaders();
    }

    [Fact]
    public async Task Cookie_Should_Not_Expose_Tenant_Info()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        var response = await client.GetAsync("/api/movimentacoes?pageSize=10");

        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                cookie.Should().NotContain("empresaId", "Cookie não deve conter empresaId");
                cookie.Should().NotContain(_fixture.EmpresaAId.ToString(), "Cookie não deve conter GUID de tenant");
            }
        }
    }

    [Fact]
    public async Task Search_With_Empty_Term_Should_Not_Return_All_Tenants()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Busca com termo vazio
        var response = await client.GetAsync("/api/produtos/search?termo=&pageSize=1000");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            // Se retorna resultados, devem ser apenas de EmpresaA
            await response.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaAId);
            await response.AssertNoItemsFromTenantAsync(_fixture.EmpresaBId);
        }
    }

    [Fact]
    public async Task Wildcard_Search_Should_Respect_Tenant_Isolation()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // Busca com wildcard (se suportado)
        var response = await client.GetAsync("/api/produtos/search?termo=%&pageSize=1000");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            await response.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaAId);
        }
    }
}
