using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Notifications.Collectors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// #3117, ponte muda sob o papel de produção (<c>rls_test_client</c>): o coletor acha a credencial da ponte de uma empresa
/// que não é a padrão (a listagem roda sob bypass de RLS) e avalia cada uma no próprio tenant. A ponte é um substituto: o
/// que se prova aqui é a ligação entre credencial, vigia, estado e aviso, não o contrato HTTP.
/// </summary>
public class ColetorPonteWppConnectMudaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Cnpj = "11222333000181";
    private const string Chat = "5511999999999@c.us";
    private const string Id = "false_5511999999999@c.us_ABC";

    private readonly MotorNotificacoesSuporte _s = new(fixture);
    private readonly IWppConnectSessaoClient _ponte = Substitute.For<IWppConnectSessaoClient>();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();

    /// <summary>Empresa padrão (recebe o aviso) e a loja, outra empresa, com a ponte ativa e o módulo ligado.</summary>
    private async Task<(Guid Padrao, Guid Loja)> PrepararAsync(bool moduloLigado = true)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();

        var padrao = Empresa.Criar("Casa da Baba Padrao", Cnpj);
        var loja = Empresa.Criar("Casa da Baba Loja", "22333444000172");
        var usuario = Usuario.Criar("Super", $"super-{Guid.NewGuid():N}@example.com", "hash");
        usuario.EmailConfirmado = true;
        var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = null, Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin, CriadoEm = DateTime.UtcNow };
        await using var db = fixture.CreateDbContext();
        using var bypass = db.UseRowLevelSecurityBypass();
        await NotificacoesGlobaisSeed.ExecutarAsync(db, NullLogger.Instance);
        db.Empresas.AddRange(padrao, loja);
        db.Perfis.Add(perfil);
        db.Set<Usuario>().Add(usuario);
        await db.SaveChangesAsync();
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, PerfilId = perfil.Id, EmpresaId = Guid.Empty, AtribuidoEm = DateTime.UtcNow
        });
        // O payload cifrado não é lido: o resolvedor é substituto. A linha existe para a listagem entre tenants.
        db.Set<CredencialIntegracao>().Add(CredencialIntegracao.Criar(loja.Id, CategoriaIntegracao.Mensageria,
            CredencialWppConnect.ProviderKey, AmbienteIntegracao.Production, [1], "kek-teste", new byte[12], new byte[16], usuario.Id));
        if (moduloLigado)
            db.TenantFeatureFlags.Add(TenantFeatureFlag.Criar(loja.Id, FeatureCatalogo.ModuloAtendimento, true, "teste@example.com"));
        await db.SaveChangesAsync();

        var sessao = new CredencialWppConnect(CredencialWppConnect.NomeSessao(loja.Id), "t", DateTimeOffset.UtcNow.AddDays(-30));
        _credenciais.ObterAsync<CredencialWppConnect>(loja.Id, CredencialWppConnect.ProviderKey, AmbienteIntegracao.Production,
            Arg.Any<CancellationToken>()).Returns(sessao);
        _ponte.Habilitado.Returns(true);
        _ponte.ObterEstadoAsync(sessao, Arg.Any<CancellationToken>()).Returns(new EstadoWppConnect(true, true, true, "CONNECTED"));
        _ponte.ListarUltimasRecebidasAsync(sessao, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new UltimaRecebidaWppConnect(Chat, Id)]);
        _ponte.ListarMensagensRecentesAsync(sessao, Chat, Arg.Any<CancellationToken>()).Returns(
            [new MensagemRecenteWppConnect(Id, DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds(), false, "chat", "SENTINELA do cliente")]);
        return (padrao.Id, loja.Id);
    }

    private ServiceProvider Provider(bool habilitado = true) => _s.ConstruirProviderDoWorker(
        papelRls: true,
        configuracao: new Dictionary<string, string?>
        {
            ["Auth:Google:EmpresaPadrao"] = Cnpj,
            [ColetorPonteWppConnectMuda.ChaveHabilitado] = habilitado ? "true" : "false",
        },
        ajustar: services =>
        {
            services.RemoveAll<IWppConnectSessaoClient>();
            services.RemoveAll<IIntegrationCredentialResolver>();
            services.AddSingleton(_ponte);
            services.AddSingleton(_credenciais);
        });

    private static async Task ColetarAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificacoesColetorOrchestrator>().ExecutarRodadaAsync();
    }

    private async Task<EndpointHealthState?> EstadoAsync(Guid loja)
    {
        await using var db = fixture.CreateDbContext();
        var nome = ColetorPonteWppConnectMuda.NomeDoEstado(loja);
        return await db.EndpointHealthStates.AsNoTracking().SingleOrDefaultAsync(s => s.EndpointName == nome);
    }

    [SkippableFact]
    public async Task PonteConectadaSemEntregarAbreOIncidenteSemDadoDoCliente()
    {
        var (padrao, loja) = await PrepararAsync();
        await using var provider = Provider();

        await ColetarAsync(provider);

        var eventos = await _s.LerEventosDaEmpresaAsync(padrao);
        eventos.Should().ContainSingle().Which.Tipo.Should().Be(TipoEventoNotificacao.IncidenteSistema);
        eventos.Single().PayloadJson.Should().NotContain("SENTINELA").And.NotContain("5511999999999");
        var estado = await EstadoAsync(loja);
        estado!.LastAlertedAt.Should().NotBeNull();
        estado.LastFailureMessage.Should().Be("PONTE_MUDA");
    }

    [SkippableFact]
    public async Task MensagemQueChegouAoWebhookNaoAbre()
    {
        var (padrao, loja) = await PrepararAsync();
        await using (var db = fixture.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            db.Set<WebhookRecebido>().Add(WebhookRecebido.Criar(ProcessarEventoWhatsAppUseCase.Provedor,
                $"wpp:{CredencialWppConnect.NomeSessao(loja)}:{Id}", "hash"));
            await db.SaveChangesAsync();
        }
        await using var provider = Provider();

        await ColetarAsync(provider);

        (await _s.LerEventosDaEmpresaAsync(padrao)).Should().BeEmpty();
        (await EstadoAsync(loja))!.LastAlertedAt.Should().BeNull();
    }

    [SkippableFact]
    public async Task DesligadoPorConfiguracaoNaoConsultaAPonte()
    {
        var (padrao, loja) = await PrepararAsync();
        await using var provider = Provider(habilitado: false);

        await ColetarAsync(provider);

        (await _s.LerEventosDaEmpresaAsync(padrao)).Should().BeEmpty();
        (await EstadoAsync(loja)).Should().BeNull();
        _ponte.ReceivedCalls().Should().BeEmpty();
    }

    [SkippableFact]
    public async Task PonteForaDoArOuModuloDesligadoNaoMudaOEstado()
    {
        var (padrao, loja) = await PrepararAsync();
        _ponte.ObterEstadoAsync(default!, default).ThrowsAsyncForAnyArgs(new HttpRequestException("http://wppconnect:21465 recusou"));
        await using var provider = Provider();

        await ColetarAsync(provider);

        (await _s.LerEventosDaEmpresaAsync(padrao)).Should().BeEmpty();
        (await EstadoAsync(loja)).Should().BeNull();

        await PrepararAsync(moduloLigado: false);
        await using var semModulo = Provider();
        await ColetarAsync(semModulo);
        await using var db = fixture.CreateDbContext();
        (await db.EndpointHealthStates.AsNoTracking().CountAsync(s => s.EndpointName.StartsWith("wppconnect/"))).Should().Be(0);
    }
}
