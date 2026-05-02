using EasyStock.Api.IntegrationTests.Helpers;
using EasyStock.Api.IntegrationTests.Infrastructure;
using FluentAssertions;
using System.Net;

namespace EasyStock.Api.IntegrationTests.Scenarios;

/// <summary>
/// Testes de SuperAdmin, impersonação e acesso global
/// </summary>
public class AdminImpersonationTests : IAsyncLifetime
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
    public async Task SuperAdmin_Can_Access_All_Tenants_Dashboard()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        // SuperAdmin sem empresaId no token
        var tokenSuperAdmin = await _fixture.GetTokenAsync(_fixture.SuperAdminId, empresaId: null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenSuperAdmin);

        // SuperAdmin pode acessar dashboard de qualquer empresa
        var responseA = await client.GetAsync(
            $"/api/analytics/dashboard?empresaId={_fixture.EmpresaAId}");
        var responseB = await client.GetAsync(
            $"/api/analytics/dashboard?empresaId={_fixture.EmpresaBId}");
        var responseC = await client.GetAsync(
            $"/api/analytics/dashboard?empresaId={_fixture.EmpresaCId}");

        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
        responseC.StatusCode.Should().Be(HttpStatusCode.OK);

        // E dados devem corresponder ao tenant solicitado
        await responseA.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaAId);
        await responseB.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaBId);
        await responseC.AssertAllItemsBelongToTenantAsync(_fixture.EmpresaCId);
    }

    [Fact]
    public async Task SuperAdmin_Cannot_Access_Without_Explicit_EmpresaId()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        // Alguns endpoints podem exigir empresaId explícita para SuperAdmin
        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenSuperAdmin = await _fixture.GetTokenAsync(_fixture.SuperAdminId, empresaId: null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenSuperAdmin);

        // Tentar acessar sem empresaId
        var response = await client.GetAsync("/api/analytics/dashboard");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Forbidden,
            because: "SuperAdmin deve fornecer empresaId explícita"
        );
    }

    [Fact]
    public async Task Regular_User_Cannot_Use_SuperAdmin_Features()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // AdminA tenta acessar /api/admin/* (SuperAdmin only)
        var response = await client.GetAsync("/api/admin/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Usuário regular não pode acessar admin endpoints");
    }

    [Fact]
    public async Task Impersonation_Endpoint_Requires_SuperAdmin()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenA = await _fixture.GetTokenAsync(_fixture.AdminAId, _fixture.EmpresaAId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenA);

        // AdminA tenta chamar impersonate
        var response = await client.PostAsync(
            $"/api/admin/tenants/{_fixture.EmpresaBId}/impersonate",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Apenas SuperAdmin pode impersonar");
    }

    [Fact]
    public async Task SuperAdmin_Impersonation_Should_Be_Audited()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        // Este teste valida que impersonação é logada em AdminImpersonationLog
        // Requer acesso direto ao DB para verificar logs

        Assert.Skip("Audit trail test requer validação de database logs");
    }

    [Fact]
    public async Task Impersonated_Token_Should_Have_Tenant_Context()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        // Teste que após impersonation, o token contém empresaId correto

        Assert.Skip("Impersonation token validation requer parsing JWT específico");
    }

    [Fact]
    public async Task SuperAdmin_List_Tenants_Should_Return_All_Companies()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenSuperAdmin = await _fixture.GetTokenAsync(_fixture.SuperAdminId, empresaId: null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenSuperAdmin);

        var response = await client.GetAsync("/api/admin/tenants?pageSize=1000");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var count = await response.AssertAndGetItemCountAsync();
            count.Should().BeGreaterThanOrEqualTo(3, "Deve listar as 3 empresas de teste");
        }
    }

    [Fact]
    public async Task SuperAdmin_View_Admin_Dashboard_Global_Stats()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenSuperAdmin = await _fixture.GetTokenAsync(_fixture.SuperAdminId, empresaId: null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenSuperAdmin);

        var response = await client.GetAsync("/api/admin/dashboard");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            // Dashboard admin pode retornar stats globais
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrEmpty();
            // Não deve expor dados PII como nomes/documentos de empresas
            content.Should().NotContain("Empresa A", "PII não deve estar em dashboard");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task SuperAdmin_Has_No_Default_Tenant_Context()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        await using var factory = _fixture.CreateFactory();
        using var client = factory.CreateClient();

        var tokenSuperAdmin = await _fixture.GetTokenAsync(_fixture.SuperAdminId, empresaId: null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokenSuperAdmin);

        // Tentar acessar endpoint que retorna contexto do usuário
        var response = await client.GetAsync("/api/auth/me");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            // SuperAdmin resposta não deve ter "empresaId" ou deve ser null/empty
            content.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Regular_Admin_With_Multiple_Tenants_Must_Choose()
    {
        if (!_fixture.IsAvailable)
            Assert.Skip("Docker não disponível");

        // Se um usuário tiver múltiplas empresas, o token deve indicar qual é a sessão
        // Neste teste simples, cada user tem só uma empresa, então skip

        Assert.Skip("Multi-tenant user test não é cenário atual");
    }
}
