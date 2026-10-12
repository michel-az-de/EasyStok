using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Auth;

/// <summary>
/// N8: o pedido de redefinição é anônimo (sem JWT, sem tenant). Sem fixar o tenant, a policy <c>tenant_isolation</c> recusa o
/// INSERT do evento (42501). Estes testes usam o papel <c>rls_test_client</c> (NOSUPERUSER) e o grafo de DI real.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class EsqueciSenhaTenantTests(PostgreSqlDatabaseFixture fixture)
{
    private const string CnpjPadrao = "11111111000191";

    [SkippableFact]
    public async Task PedidoAnonimoGravaOEventoNoTenantDoUsuario()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();
        var (padrao, outra) = await SemearEmpresasAsync();
        var usuario = await SemearUsuarioAsync(vinculadoA: outra);

        await PedirAnonimoAsync(usuario.Email);

        var evento = await EventoDoPedidoAsync();
        evento.EmpresaId.Should().Be(outra.Id, "o usuario tem uma so empresa ativa");
        evento.EmpresaId.Should().NotBe(padrao.Id);
        evento.Tipo.Should().Be(TipoEventoNotificacao.ResetSenha);
        evento.Status.Should().Be(StatusEventoNotificacao.Pendente);
    }

    [SkippableFact]
    public async Task SuperadminGravaNaEmpresaPadrao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();
        var (padrao, _) = await SemearEmpresasAsync();
        var superadmin = await SemearUsuarioAsync(vinculadoA: null, superAdmin: true);

        await PedirAnonimoAsync(superadmin.Email);

        var evento = await EventoDoPedidoAsync();
        evento.EmpresaId.Should().Be(padrao.Id, "superadmin nao tem empresa: o evento nasce na empresa padrao");
        System.Text.Json.JsonDocument.Parse(evento.PayloadJson).RootElement.GetProperty("canais")[0].GetString()
            .Should().Be("Email", "superadmin nunca recebe o codigo por WhatsApp");
    }

    private async Task PedirAnonimoAsync(string email)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:LinkRedefinirSenha"] = "https://app.easystok.com.br/auth/redefinir-senha?token={0}",
                ["Auth:Google:EmpresaPadrao"] = CnpjPadrao,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>()); // sem JWT: IsAuthenticated=false
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton(Substitute.For<IRendererTemplate>());
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddEasyStockPostgreInfrastructure(fixture.RlsClientConnectionString, config);
        services.AddEasyStockApplication();
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var resultado = await scope.ServiceProvider.GetRequiredService<EsqueciSenhaUseCase>()
            .ExecuteAsync(new EsqueciSenhaCommand(email, "203.0.113.7", "TesteIntegracao/1.0"));
        resultado.Success.Should().BeTrue();
    }

    private async Task<EventoNotificacao> EventoDoPedidoAsync()
    {
        // Leitura pelo papel dono (sem RLS): o que importa e onde o INSERT do evento caiu.
        await using var db = fixture.CreateDbContext();
        var eventos = await db.NotifEventos.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Tipo == TipoEventoNotificacao.ResetSenha).ToListAsync();
        return eventos.Should().ContainSingle("um pedido gera um evento so").Subject;
    }

    private async Task<(Empresa Padrao, Empresa Outra)> SemearEmpresasAsync()
    {
        var padrao = Empresa.Criar("Casa da Baba", CnpjPadrao);
        var outra = Empresa.Criar("Outra Loja", "22222222000191");
        await using var db = fixture.CreateDbContext();
        db.Set<Empresa>().AddRange(padrao, outra);
        await db.SaveChangesAsync();
        return (padrao, outra);
    }

    private async Task<Usuario> SemearUsuarioAsync(Empresa? vinculadoA, bool superAdmin = false)
    {
        var usuario = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@casadababa.com", "hash");
        await using var db = fixture.CreateDbContext();
        db.Usuarios.Add(usuario);
        if (vinculadoA is not null)
            db.Set<UsuarioEmpresa>().Add(new UsuarioEmpresa
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = vinculadoA.Id, Ativo = true, CriadoEm = DateTime.UtcNow,
            });
        if (superAdmin)
        {
            var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = null, Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin, CriadoEm = DateTime.UtcNow };
            db.Set<Perfil>().Add(perfil);
            db.Set<UsuarioPerfil>().Add(new UsuarioPerfil
            {
                Id = Guid.NewGuid(), UsuarioId = usuario.Id, PerfilId = perfil.Id, EmpresaId = Guid.Empty, AtribuidoEm = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        return usuario;
    }
}
