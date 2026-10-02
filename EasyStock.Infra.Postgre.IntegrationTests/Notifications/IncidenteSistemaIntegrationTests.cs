using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.Email;
using EasyStock.Infra.Notifications.Email;
using EasyStock.Infra.Notifications.Templating;
using EasyStock.Infra.Postgre.IntegrationTests.Email;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N10 ponta a ponta no e-mail: o publicador roda como no Worker (papel <c>rls_test_client</c>, empresa padrão resolvida pela
/// configuração, tenant ligado no escopo), o catálogo vem do seed da N13 e a audiência <c>superadmins</c> da N4. A dedupe de
/// 15 min é a chave de idempotência do outbox, sem tabela nova. A perna WhatsApp fecha com a N6.
/// </summary>
[Collection("Mailpit")]
public class IncidenteSistemaIntegrationTests(MailpitFixture mailpit, PostgreSqlDatabaseFixture postgres)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Avisos = "avisos@easystok.online";
    private const string Cnpj = "11222333000181";

    private readonly MotorNotificacoesSuporte _s = new(postgres);
    private RelogioManual _relogio = null!;

    private SmtpEmailCanal CriarCanalSmtp()
    {
        var configuracao = new SmtpOpcoes
        {
            Host = mailpit.Host,
            Port = mailpit.PortaSmtp.ToString(),
            Modo = "Nenhum",
            FromEmail = Avisos,
            FromName = "EasyStok Avisos",
            Seguranca = new SmtpRemetenteOpcoes { FromEmail = "seguranca@easystok.online", FromName = "EasyStok Segurança" },
        }.Resolver("Development");

        return new SmtpEmailCanal(new SmtpEmailService(configuracao, NullLogger<SmtpEmailService>.Instance), NullLogger<SmtpEmailCanal>.Instance);
    }

    private async Task<(Guid EmpresaId, string[] Superadmins, ServiceProvider Provider)> PrepararAsync()
    {
        Skip.If(!postgres.IsAvailable, postgres.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        Skip.If(!mailpit.IsAvailable, mailpit.UnavailableReason ?? "Docker/Mailpit indisponivel");
        await mailpit.LimparAsync();
        await postgres.ResetDatabaseAsync();

        // Início da janela de 15 min: os avisos "na mesma janela" ficam dentro dela sem depender do relógio da máquina.
        var agora = DateTimeOffset.UtcNow;
        var inicioDaJanela = DateTimeOffset.FromUnixTimeSeconds(agora.ToUnixTimeSeconds() / 900 * 900);
        _relogio = new RelogioManual(inicioDaJanela.AddMinutes(1));

        var empresa = Empresa.Criar("Casa da Baba Padrao", Cnpj);
        var emails = new[] { $"super-a-{Guid.NewGuid():N}@example.com", $"super-b-{Guid.NewGuid():N}@example.com" };
        await using (var db = postgres.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            await NotificacoesGlobaisSeed.ExecutarAsync(db, NullLogger.Instance);
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync();

            var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = null, Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin, CriadoEm = DateTime.UtcNow };
            db.Perfis.Add(perfil);
            await db.SaveChangesAsync();
            foreach (var email in emails)
            {
                var usuario = Usuario.Criar("Super", email, "hash");
                usuario.EmailConfirmado = true;
                db.Set<Usuario>().Add(usuario);
                await db.SaveChangesAsync();
                db.UsuariosPerfis.Add(new UsuarioPerfil
                {
                    Id = Guid.NewGuid(), UsuarioId = usuario.Id, PerfilId = perfil.Id, EmpresaId = Guid.Empty, AtribuidoEm = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }

        var provider = _s.ConstruirProviderDoWorker(
            papelRls: true,
            configuracao: new Dictionary<string, string?> { ["Auth:Google:EmpresaPadrao"] = Cnpj },
            ajustar: services =>
            {
                services.AddSingleton<IRendererTemplate>(new ScribanRenderer(NullLogger<ScribanRenderer>.Instance));
                services.AddSingleton<TimeProvider>(_relogio);
            },
            canais: CriarCanalSmtp());
        return (empresa.Id, emails, provider);
    }

    private async Task PublicarEProcessarAsync(
        ServiceProvider provider, EstadoIncidente estado, TimeSpan duracao, int repeticoes = 1)
    {
        for (var i = 0; i < repeticoes; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IPublicadorIncidenteSistema>().PublicarAsync(
                ComponenteIncidente.Api, estado, SeveridadeIncidente.Alta, _relogio.GetUtcNow().UtcDateTime - duracao);
        }

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));
        await provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount: 4, batchSize: 50);
    }

    [SkippableFact]
    public async Task DuasFalhasNaMesmaJanelaGeramUmaMensagemPorDestinatarioECanal()
    {
        var (empresaId, superadmins, provider) = await PrepararAsync();
        await using var _ = provider;

        await PublicarEProcessarAsync(provider, EstadoIncidente.ComProblema, TimeSpan.FromMinutes(3), repeticoes: 2);

        var mensagens = await _s.LerMensagensDaEmpresaAsync(empresaId);
        mensagens.Should().HaveCount(2, "duas falhas na mesma janela de 15 min valem um aviso por superadmin e por canal");
        mensagens.Select(m => m.Destinatario).Should().BeEquivalentTo(superadmins);
        mensagens.Select(m => m.IdempotencyKey).Distinct().Should().HaveCount(2, "a chave inclui o destinatário");
        (await mailpit.AguardarAsync(2)).Should().HaveCount(2);
        (await mailpit.ContarAsync()).Should().Be(2);
    }

    [SkippableFact]
    public async Task JanelaSeguinteGeraNovoAviso()
    {
        var (empresaId, superadmins, provider) = await PrepararAsync();
        await using var _ = provider;

        await PublicarEProcessarAsync(provider, EstadoIncidente.ComProblema, TimeSpan.FromMinutes(3));
        _relogio.Advance(TimeSpan.FromMinutes(15));
        await PublicarEProcessarAsync(provider, EstadoIncidente.ComProblema, TimeSpan.FromMinutes(18));

        var mensagens = await _s.LerMensagensDaEmpresaAsync(empresaId);
        mensagens.Should().HaveCount(superadmins.Length * 2, "a janela seguinte é uma chave nova");
        (await mailpit.AguardarAsync(4)).Should().HaveCount(4);
    }

    [SkippableFact]
    public async Task ResolvidoSaiDepoisDoAberto()
    {
        var (empresaId, _, provider) = await PrepararAsync();
        await using var __ = provider;

        await PublicarEProcessarAsync(provider, EstadoIncidente.ComProblema, TimeSpan.FromMinutes(1));
        await PublicarEProcessarAsync(provider, EstadoIncidente.Normalizado, TimeSpan.FromMinutes(7));

        var mensagens = await _s.LerMensagensDaEmpresaAsync(empresaId);
        mensagens.Should().HaveCount(4);
        var assuntos = mensagens.OrderBy(m => m.CriadoEm).Select(m => m.AssuntoRenderizado).ToList();
        assuntos.Take(2).Should().OnlyContain(a => a == "EasyStok: API do EasyStok com problema");
        assuntos.Skip(2).Should().OnlyContain(a => a == "EasyStok: API do EasyStok normalizado");
        var normalizados = (await mailpit.AguardarAsync(4)).Where(m => m.Assunto.Contains("normalizado")).ToList();
        normalizados.Should().HaveCount(2);
        normalizados.Should().OnlyContain(m => m.Html!.Contains("7 minutos"), "o aviso de resolvido leva a duração");
    }

    [SkippableFact]
    public async Task ChegaNoMailpitComRemetenteDeAvisos()
    {
        var (_, superadmins, provider) = await PrepararAsync();
        await using var _ = provider;

        await PublicarEProcessarAsync(provider, EstadoIncidente.ComProblema, TimeSpan.FromMinutes(3));

        var recebidas = await mailpit.AguardarAsync(2);
        recebidas.Select(m => m.DeEndereco).Should().OnlyContain(d => d == Avisos);
        recebidas.SelectMany(m => m.Para).Should().BeEquivalentTo(superadmins);
        recebidas.Should().OnlyContain(m => m.Assunto == "EasyStok: API do EasyStok com problema");
        recebidas.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m.Html) && !m.Html!.Contains("{{"));
    }
}

/// <summary>Relógio que só anda quando o teste manda (evita um pacote só para isto).</summary>
internal sealed class RelogioManual(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _agora = inicio;

    public override DateTimeOffset GetUtcNow() => _agora;

    public void Advance(TimeSpan tempo) => _agora += tempo;
}
