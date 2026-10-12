using EasyStock.Api.Data;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N10, pico de 5xx sob o papel de produção (<c>rls_test_client</c>): <c>system_error_logs</c> fica fora da RLS, então o
/// Worker conta sem bypass próprio, e o coletor nunca carrega <c>Message</c> nem <c>Details</c> para o aviso.
/// </summary>
public class ColetorPicoDeErros5xxIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Cnpj = "11222333000181";
    private const string Sentinela = "SENTINELA-maria@cliente.com?token=XYZ";

    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private async Task<(Guid EmpresaId, Guid SuperadminId)> PrepararAsync()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();

        var empresa = Empresa.Criar("Casa da Baba Padrao", Cnpj);
        var usuario = Usuario.Criar("Super", $"super-{Guid.NewGuid():N}@example.com", "hash");
        usuario.EmailConfirmado = true;
        var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = null, Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin, CriadoEm = DateTime.UtcNow };
        await using var db = fixture.CreateDbContext();
        using var bypass = db.UseRowLevelSecurityBypass();
        await NotificacoesGlobaisSeed.ExecutarAsync(db, NullLogger.Instance);
        db.Empresas.Add(empresa);
        db.Perfis.Add(perfil);
        db.Set<Usuario>().Add(usuario);
        await db.SaveChangesAsync();
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, PerfilId = perfil.Id, EmpresaId = Guid.Empty, AtribuidoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (empresa.Id, usuario.Id);
    }

    private async Task SemearErrosAsync(int quantidade, string source = "api_backend", string level = "error", DateTime? quando = null)
    {
        await using var db = fixture.CreateDbContext();
        using var bypass = db.UseRowLevelSecurityBypass();
        for (var i = 0; i < quantidade; i++)
            db.SystemErrorLogs.Add(new SystemErrorLog
            {
                Id = Guid.NewGuid(), Source = source, Level = level, Message = Sentinela, Details = $"{{\"stackTrace\":\"{Sentinela}\"}}",
                CriadoEm = quando ?? DateTime.UtcNow,
            });
        await db.SaveChangesAsync();
    }

    private ServiceProvider Provider() => _s.ConstruirProviderDoWorker(
        papelRls: true,
        configuracao: new Dictionary<string, string?>
        {
            ["Auth:Google:EmpresaPadrao"] = Cnpj,
            ["Notifications:Incidentes:Erros5xx:Limite"] = "3",
            ["Notifications:Incidentes:Erros5xx:JanelaMinutos"] = "5",
        });

    private static async Task ColetarAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificacoesColetorOrchestrator>().ExecutarRodadaAsync();
    }

    private static async Task AvaliarAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));
    }

    [SkippableFact]
    public async Task AbaixoDoLimiteNaoAbre()
    {
        var (empresaId, _) = await PrepararAsync();
        await SemearErrosAsync(3); // limite 3: acima de 3 abre
        await SemearErrosAsync(50, source: "web_frontend");
        await SemearErrosAsync(50, level: "warning");
        await SemearErrosAsync(50, quando: DateTime.UtcNow.AddMinutes(-30));
        await using var provider = Provider();

        await ColetarAsync(provider);

        (await _s.LerEventosDaEmpresaAsync(empresaId)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task AcimaDoLimiteAbreUmaVez()
    {
        var (empresaId, superadminId) = await PrepararAsync();
        await SemearErrosAsync(4);
        await using var provider = Provider();

        await ColetarAsync(provider);
        await ColetarAsync(provider); // a rodada seguinte re-emite, e a chave de idempotência da janela segura a duplicata
        await AvaliarAsync(provider);

        var eventos = await _s.LerEventosDaEmpresaAsync(empresaId);
        eventos.Should().OnlyContain(e => e.Tipo == TipoEventoNotificacao.IncidenteSistema);
        var mensagens = await _s.LerMensagensDaEmpresaAsync(empresaId);
        mensagens.Should().ContainSingle("um aviso por janela de dedupe e por superadmin").Which.UsuarioDestinoId.Should().Be(superadminId);

        await using var db = fixture.CreateDbContext();
        var estado = await db.EndpointHealthStates.AsNoTracking().SingleAsync(s => s.EndpointName == "sistema/5xx");
        estado.LastAlertedAt.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task NaoLeMensagemNemDetalhes()
    {
        var (empresaId, _) = await PrepararAsync();
        await SemearErrosAsync(10);
        await using var provider = Provider();

        await ColetarAsync(provider);
        await AvaliarAsync(provider);

        var eventos = await _s.LerEventosDaEmpresaAsync(empresaId);
        eventos.Should().NotBeEmpty();
        eventos.Select(e => e.PayloadJson).Should().OnlyContain(p => !p.Contains("SENTINELA") && !p.Contains("token"));
        var mensagens = await _s.LerMensagensDaEmpresaAsync(empresaId);
        mensagens.Should().NotBeEmpty();
        mensagens.Should().OnlyContain(m =>
            !m.CorpoRenderizado.Contains("SENTINELA") && !m.AssuntoRenderizado.Contains("SENTINELA"));
        await using var db = fixture.CreateDbContext();
        (await db.EndpointHealthStates.AsNoTracking().SingleAsync(s => s.EndpointName == "sistema/5xx"))
            .LastFailureMessage.Should().Be("5XX_10", "o estado guarda a contagem, nunca texto de erro");
    }
}
