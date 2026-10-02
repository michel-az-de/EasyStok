using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Api.Data;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Application.UseCases.Operacao.Atraso;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.Email;
using EasyStock.Infra.Notifications.Email;
using EasyStock.Infra.Notifications.Templating;
using EasyStock.Infra.Postgre.IntegrationTests.Email;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N11 ponta a ponta no e-mail: o pedido atrasado enfileira o <c>PrazoEstourado</c> no caminho real (use case do job,
/// Avaliador e Dispatcher como <c>rls_test_client</c>, audiência <c>gestores</c> da rotina global do seed, Scriban e
/// <c>SmtpEmailCanal</c>) e chega num Mailpit real, para o gestor da empresa, com o remetente de avisos.
/// </summary>
[Collection("Mailpit")]
public class PrazoEstouradoChegaNoMailpitIntegrationTests(MailpitFixture mailpit, PostgreSqlDatabaseFixture postgres)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Avisos = "avisos@easystok.online";

    [SkippableFact]
    public async Task PedidoAtrasadoChegaPorEmailComRemetenteDeAvisos()
    {
        Skip.If(!postgres.IsAvailable, postgres.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        Skip.If(!mailpit.IsAvailable, mailpit.UnavailableReason ?? "Docker/Mailpit indisponivel");
        await mailpit.LimparAsync();
        await postgres.ResetDatabaseAsync();

        await using (var db = postgres.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            await NotificacoesGlobaisSeed.ExecutarAsync(db, NullLogger.Instance);
            // A janela 07:00-22:00 da rotina é da N5 (testada à parte); aqui o relógio real não pode adiar o e-mail.
            var rotina = await db.NotifRotinas.IgnoreQueryFilters().SingleAsync(r => r.Codigo == "prazo_estourado_global" && r.EmpresaId == null);
            rotina.JanelaInicio = null;
            rotina.JanelaFim = null;
            await db.SaveChangesAsync();
        }

        var suporte = new MotorNotificacoesSuporte(postgres);
        var empresaId = await suporte.SemearEmpresaAsync();
        var gestorEmail = $"gestora-{Guid.NewGuid():N}@example.com";
        await SemearGestorAsync(empresaId, gestorEmail);
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = StatusPedidoMapper.Aguardando;
        pedido.DefinirInicioPrevisto(DateTime.UtcNow.AddMinutes(-35));
        await using (var db = postgres.CreateDbContext())
        {
            using var bypass = db.UseRowLevelSecurityBypass();
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
        }

        await using var provider = suporte.ConstruirProviderDoWorker(
            papelRls: true,
            ajustar: services => services.AddSingleton<IRendererTemplate>(new ScribanRenderer(NullLogger<ScribanRenderer>.Instance)),
            canais: CriarCanalSmtp());

        await using (var escopo = provider.CreateAsyncScope())
        {
            var candidatos = await escopo.ServiceProvider.GetRequiredService<IPedidoStorefrontRepository>()
                .ListarAtrasoNaoNotificadoAsync(DateTime.UtcNow, 100);
            candidatos.Should().Contain(c => c.PedidoId == pedido.Id);
        }

        await using (var escopo = provider.CreateAsyncScope())
            await escopo.ServiceProvider.GetRequiredService<NotificarAtrasoPedidoUseCase>()
                .ExecuteAsync(new PedidoAtrasoCandidato(pedido.Id, empresaId));
        await using (var escopo = provider.CreateAsyncScope())
            await escopo.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>().ExecutarRodadaAsync(TimeSpan.FromMinutes(2));
        await provider.GetRequiredService<INotificacoesDispatcherOrchestrator>().ExecutarRodadaAsync(shardCount: 4, batchSize: 50);

        var evento = (await suporte.LerEventosDaEmpresaAsync(empresaId)).Should()
            .ContainSingle(e => e.Tipo == TipoEventoNotificacao.PrazoEstourado).Subject;
        evento.Status.Should().Be(StatusEventoNotificacao.Processado, evento.ErroProcessamento);
        var mensagem = (await mailpit.AguardarAsync(1)).Should().ContainSingle().Subject;
        mensagem.DeEndereco.Should().Be(Avisos);
        mensagem.Para.Should().Equal(gestorEmail);
        mensagem.Assunto.Should().Contain("Pedido sem início de preparo").And.Contain("35 minutos");
        mensagem.Html.Should().NotBeNullOrWhiteSpace().And.NotContain("{{");
    }

    private async Task SemearGestorAsync(Guid empresaId, string email)
    {
        var usuario = Usuario.Criar("Gestora", email, "hash");
        usuario.EmailConfirmado = true;
        var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Gerente", Nivel = NivelAcesso.Gerente, CriadoEm = DateTime.UtcNow };

        await using var db = postgres.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Usuarios.Add(usuario);
        db.Perfis.Add(perfil);
        await db.SaveChangesAsync();
        db.UsuariosEmpresas.Add(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresaId, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, PerfilId = perfil.Id, EmpresaId = empresaId, AtribuidoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

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
}
