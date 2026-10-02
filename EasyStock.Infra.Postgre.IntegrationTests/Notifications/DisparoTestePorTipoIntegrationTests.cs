using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.Email;
using EasyStock.Infra.Notifications.Email;
using EasyStock.Infra.Notifications.Templating;
using EasyStock.Infra.Postgre.IntegrationTests.Email;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N13 ponta a ponta no e-mail: o disparo de teste de cada tipo do catálogo passa pelo caminho real (seed do catálogo, use
/// case, Avaliador e Dispatcher como <c>rls_test_client</c>, os mesmos pontos de entrada que o Worker usa depois da N1,
/// renderizador Scriban e <c>SmtpEmailCanal</c> com MailKit) e chega num Mailpit real com o remetente da categoria do
/// catálogo e o assunto <c>[TESTE] ...</c>. <c>ResetSenha</c> e <c>ConviteAcesso</c> chegam de <c>seguranca@</c>, os demais
/// de <c>avisos@</c>. A perna WhatsApp fecha com a N5 e a N6.
/// </summary>
[Collection("Mailpit")]
public class DisparoTestePorTipoIntegrationTests(MailpitFixture mailpit, PostgreSqlDatabaseFixture postgres)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Avisos = "avisos@easystok.online";
    private const string Seguranca = "seguranca@easystok.online";

    public static TheoryData<TipoEventoNotificacao, string> Tipos => new()
    {
        { TipoEventoNotificacao.ResetSenha, Seguranca },
        { TipoEventoNotificacao.ConviteAcesso, Seguranca },
        { TipoEventoNotificacao.IncidenteSistema, Avisos },
        { TipoEventoNotificacao.PrazoEstourado, Avisos },
        { TipoEventoNotificacao.ResumoDiario, Avisos },
    };

    private SmtpEmailCanal CriarCanalSmtp()
    {
        var configuracao = new SmtpOpcoes
        {
            Host = mailpit.Host,
            Port = mailpit.PortaSmtp.ToString(),
            Modo = "Nenhum",
            FromEmail = Avisos,
            FromName = "EasyStok Avisos",
            Seguranca = new SmtpRemetenteOpcoes { FromEmail = Seguranca, FromName = "EasyStok Segurança" },
        }.Resolver("Development");

        return new SmtpEmailCanal(new SmtpEmailService(configuracao, NullLogger<SmtpEmailService>.Instance), NullLogger<SmtpEmailCanal>.Instance);
    }

    [SkippableTheory]
    [MemberData(nameof(Tipos))]
    public async Task CadaTipoChegaNoMailpitComORemetenteDoCatalogo(TipoEventoNotificacao tipo, string remetenteEsperado)
    {
        Skip.If(!postgres.IsAvailable, postgres.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        Skip.If(!mailpit.IsAvailable, mailpit.UnavailableReason ?? "Docker/Mailpit indisponivel");
        await mailpit.LimparAsync();
        await postgres.ResetDatabaseAsync();

        await using (var db = postgres.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            await NotificacoesGlobaisSeed.ExecutarAsync(db, NullLogger.Instance);

            // resumo_diario_global nasce inativa (molde; a N12 liga por empresa). Para provar o template ponta a ponta,
            // o teste liga a rotina como a N12 fara.
            if (tipo == TipoEventoNotificacao.ResumoDiario)
            {
                var rotina = await db.NotifRotinas.IgnoreQueryFilters().SingleAsync(r => r.Codigo == "resumo_diario_global" && r.EmpresaId == null);
                rotina.Ativar("teste");
                await db.SaveChangesAsync();
            }
        }

        var suporte = new MotorNotificacoesSuporte(postgres);
        var empresaId = await suporte.SemearEmpresaAsync();
        var emailDoSuperadmin = $"superadmin-{tipo}@example.com".ToLowerInvariant();
        var superadmin = Usuario.Criar("Felipe", emailDoSuperadmin, "hash");
        superadmin.EmailConfirmado = true;
        await using (var db = postgres.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            db.Set<Usuario>().Add(superadmin);
            await db.SaveChangesAsync();
        }

        await using var provider = suporte.ConstruirProviderDoWorker(
            papelRls: true,
            ajustar: services => services.AddSingleton<IRendererTemplate>(new ScribanRenderer(NullLogger<ScribanRenderer>.Instance)),
            canais: CriarCanalSmtp());

        Guid eventoId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var servicos = scope.ServiceProvider;
            var usuarioAtual = Substitute.For<ICurrentUserAccessor>();
            usuarioAtual.UsuarioId.Returns(superadmin.Id);
            usuarioAtual.EmpresaId.Returns(empresaId);
            var usuarios = Substitute.For<IUsuarioRepository>();
            usuarios.GetByIdAsync(superadmin.Id).Returns(superadmin);

            var disparo = new DispararTesteNotificacaoUseCase(
                usuarioAtual, usuarios, Substitute.For<IEmpresaPadraoResolver>(),
                servicos.GetRequiredService<INotificadorService>(),
                servicos.GetRequiredService<IEventoNotificacaoRepository>(),
                servicos.GetRequiredService<IOutboxNotificacaoRepository>(),
                servicos.GetRequiredService<ITenantContextAccessor>(),
                servicos.GetRequiredService<IUnitOfWork>(),
                NullLogger<DispararTesteNotificacaoUseCase>.Instance);
            eventoId = await disparo.ExecuteAsync(tipo);
        }

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));
        await provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount: 4, batchSize: 50);

        var evento = await suporte.LerEventoAsync(eventoId);
        evento.Status.Should().Be(StatusEventoNotificacao.Processado, evento.ErroProcessamento);
        var outbox = (await suporte.LerMensagensDoEventoAsync(eventoId)).Should().ContainSingle().Subject;
        outbox.Status.Should().Be(StatusOutbox.Enviado);
        outbox.Canal.Should().Be(CanalNotificacao.Email);
        var mensagem = (await mailpit.AguardarAsync(1)).Should().ContainSingle().Subject;
        mensagem.DeEndereco.Should().Be(remetenteEsperado);
        mensagem.Para.Should().Equal(emailDoSuperadmin);
        mensagem.Assunto.Should().StartWith("[TESTE] ");
        mensagem.Html.Should().NotBeNullOrWhiteSpace().And.NotContain("{{");

    }
}
