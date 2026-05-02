using EasyStock.Api.Services;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Fixture para testes E2E multi-tenant com 3 empresas isoladas.
/// Gerencia PostgreSQL container, seed de dados e geração de tokens JWT.
/// </summary>
public sealed class MultiTenantTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;
    private EasyStockDbContext? _dbContext;

    public bool IsAvailable => _isAvailable;

    // Tenant IDs (fixed para determinismo)
    public Guid EmpresaAId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid EmpresaBId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public Guid EmpresaCId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // User IDs (fixed para determinismo)
    public Guid AdminAId { get; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid AdminBId { get; } = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public Guid AdminCId { get; } = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public Guid SuperAdminId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000000");

    // JWT Config
    private const string JwtIssuer = "EasyStock";
    private const string JwtAudience = "EasyStock";
    private const string JwtSecret = "EasyStock-Test-SuperSecretKey-Min32Chars!!";

    public string ConnectionString => _pg?.GetConnectionString() ?? throw new InvalidOperationException("DB não disponível");

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_multitenant_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _pg.StartAsync();
            _isAvailable = true;

            // Criar contexto e aplicar migrations
            var factory = CreateFactory();
            using var scope = factory.Services.CreateScope();
            _dbContext = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            await _dbContext.Database.EnsureCreatedAsync();

            // Seed multi-tenant data
            await SeedMultiTenantDataAsync();
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        _dbContext?.Dispose();
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    /// <summary>
    /// Cria WebApplicationFactory configurada para testes multi-tenant
    /// </summary>
    public WebApplicationFactory<Program> CreateFactory()
    {
        if (_pg is null) throw new InvalidOperationException("Contêiner PostgreSQL não disponível.");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"] = "PostgreSql",
                        ["ConnectionStrings:DefaultConnection"] = _pg.GetConnectionString(),
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        ["Jwt:Issuer"] = JwtIssuer,
                        ["Jwt:Audience"] = JwtAudience,
                        ["Jwt:SecretKey"] = JwtSecret,
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Anthropic:Enabled"] = "false",
                        ["FileStorage:Provider"] = "Local"
                    });
                });
            });
    }

    /// <summary>
    /// Gera JWT token para usuário/empresa
    /// </summary>
    public async Task<string> GetTokenAsync(Guid usuarioId, Guid? empresaId)
    {
        if (_dbContext is null) throw new InvalidOperationException("DB context não disponível");

        var usuario = await _dbContext.Usuarios.FindAsync(usuarioId);
        usuario.Should().NotBeNull($"Usuário {usuarioId} não encontrado no seed");

        // Usar JwtTokenService da aplicação ou simular aqui
        var token = GenerateJwtToken(usuarioId, usuario.Email ?? "test@example.com", empresaId);
        return token;
    }

    /// <summary>
    /// Seed de dados para 3 empresas isoladas
    /// </summary>
    private async Task SeedMultiTenantDataAsync()
    {
        var db = _dbContext ?? throw new InvalidOperationException("DB context não inicializado");

        // Limpar dados existentes
        db.Empresas.RemoveRange(db.Empresas);
        db.Usuarios.RemoveRange(db.Usuarios);
        db.UsuariosEmpresas.RemoveRange(db.UsuariosEmpresas);
        await db.SaveChangesAsync();

        // Create companies
        var empresaA = new Empresa
        {
            Id = EmpresaAId,
            Nome = "Empresa A",
            Documento = "11111111111111",
            CriadoEm = DateTime.UtcNow
        };
        var empresaB = new Empresa
        {
            Id = EmpresaBId,
            Nome = "Empresa B",
            Documento = "22222222222222",
            CriadoEm = DateTime.UtcNow
        };
        var empresaC = new Empresa
        {
            Id = EmpresaCId,
            Nome = "Empresa C",
            Documento = "33333333333333",
            CriadoEm = DateTime.UtcNow
        };

        db.Empresas.AddRange(empresaA, empresaB, empresaC);

        // Create users (hash password)
        var adminA = new Usuario
        {
            Id = AdminAId,
            Email = "admin.a@test.com",
            Nome = "Admin A",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("AdminA@2026!"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var adminB = new Usuario
        {
            Id = AdminBId,
            Email = "admin.b@test.com",
            Nome = "Admin B",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("AdminB@2026!"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var adminC = new Usuario
        {
            Id = AdminCId,
            Email = "admin.c@test.com",
            Nome = "Admin C",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("AdminC@2026!"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };
        var superAdmin = new Usuario
        {
            Id = SuperAdminId,
            Email = "superadmin@test.com",
            Nome = "Super Admin",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("SuperAdmin@2026!"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        };

        db.Usuarios.AddRange(adminA, adminB, adminC, superAdmin);

        // Link users to companies
        db.UsuariosEmpresas.AddRange(
            new UsuarioEmpresa { UsuarioId = AdminAId, EmpresaId = EmpresaAId, Ativo = true },
            new UsuarioEmpresa { UsuarioId = AdminBId, EmpresaId = EmpresaBId, Ativo = true },
            new UsuarioEmpresa { UsuarioId = AdminCId, EmpresaId = EmpresaCId, Ativo = true }
        );

        await db.SaveChangesAsync();

        // Seed company-specific data
        await SeedCompanyDataAsync(EmpresaAId, productCount: 50, movementCount: 100);
        await SeedCompanyDataAsync(EmpresaBId, productCount: 75, movementCount: 150);
        await SeedCompanyDataAsync(EmpresaCId, productCount: 30, movementCount: 80);
    }

    /// <summary>
    /// Seed dados específicos por empresa (produtos, categorias, movimentações, etc)
    /// </summary>
    private async Task SeedCompanyDataAsync(Guid empresaId, int productCount, int movementCount)
    {
        var db = _dbContext ?? throw new InvalidOperationException("DB context não inicializado");

        // Create category
        var categoria = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = $"Categoria {empresaId:N}",
            Descricao = "Test category",
            CriadoEm = DateTime.UtcNow
        };
        db.Categorias.Add(categoria);
        await db.SaveChangesAsync();

        // Create products
        var random = new Random((int)empresaId.GetHashCode()); // Deterministic but different per company
        var produtos = new List<Produto>();
        for (int i = 0; i < productCount; i++)
        {
            var produto = new Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                CategoriaId = categoria.Id,
                Nome = $"Produto {i + 1} - Empresa {empresaId:N}",
                SkuBase = $"SKU-{empresaId:N}-{i:D4}",
                CodigoBarrasEan = $"EAN{Random.Shared.Next(1000000000, 9999999999)}",
                PrecoVenda = decimal.Round((decimal)(10 + random.NextDouble() * 100), 2),
                PrecoCusto = decimal.Round((decimal)(5 + random.NextDouble() * 50), 2),
                QuantidadeMinimaPadrao = random.Next(5, 50),
                Status = EasyStock.Domain.Enums.ProdutoStatus.Ativo,
                CriadoEm = DateTime.UtcNow
            };
            produtos.Add(produto);
        }
        db.Produtos.AddRange(produtos);
        await db.SaveChangesAsync();

        // Create item estoque
        foreach (var produto in produtos)
        {
            var itemEstoque = new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produto.Id,
                Quantidade = random.Next(50, 500),
                QuantidadeBloqueada = random.Next(0, 20),
                QuantidadeReservada = random.Next(0, 10),
                DataValidade = DateTime.UtcNow.AddMonths(random.Next(1, 24)),
                Status = EasyStock.Domain.Enums.StatusItemEstoque.Ativo,
                CriadoEm = DateTime.UtcNow
            };
            db.ItensEstoque.Add(itemEstoque);
        }
        await db.SaveChangesAsync();

        // Create movimentações
        for (int i = 0; i < movementCount; i++)
        {
            var itemEstoque = db.ItensEstoque
                .Where(x => x.EmpresaId == empresaId)
                .OrderBy(x => random.Next())
                .First();

            var movimentacao = new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ItemEstoqueId = itemEstoque.Id,
                Tipo = i % 2 == 0 ? EasyStock.Domain.Enums.TipoMovimentacao.Entrada : EasyStock.Domain.Enums.TipoMovimentacao.Saida,
                Natureza = EasyStock.Domain.Enums.NaturezaMovimentacao.Venda,
                Quantidade = random.Next(1, 20),
                UsuarioId = empresaId == EmpresaAId ? AdminAId : empresaId == EmpresaBId ? AdminBId : AdminCId,
                CriadoEm = DateTime.UtcNow.AddDays(-random.Next(0, 30))
            };
            db.MovimentacoesEstoque.Add(movimentacao);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Simula geração de JWT (em testes, usamos um token simplificado)
    /// </summary>
    private static string GenerateJwtToken(Guid usuarioId, string email, Guid? empresaId)
    {
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: new[]
            {
                new System.Security.Claims.Claim("sub", usuarioId.ToString()),
                new System.Security.Claims.Claim("email", email),
                new System.Security.Claims.Claim("nivel",
                    usuarioId.ToString() == "00000000-0000-0000-0000-000000000000" ? "SuperAdmin" : "Admin"),
                empresaId.HasValue ? new System.Security.Claims.Claim("empresaId", empresaId.Value.ToString()) : null
            }.Where(x => x is not null).ToArray(),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(JwtSecret)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
