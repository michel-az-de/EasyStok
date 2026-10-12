using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N5: escolha de canal e template por tipo, modo <c>todos</c>, chave <c>canais</c> e pausa da empresa.</summary>
public class NotificadorServiceCanalTests
{
    [Fact]
    public async Task PrimarioBloqueadoUsaOProximoCanalComTemplateProprio()
    {
        // O InApp não está ativo no tenant: o primeiro canal permitido é o e-mail, que tem template por tipo e canal.
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        f.UsarRotina(["InApp", "Email"]);
        f.UsarTemplate(CanalNotificacao.Email, assunto: "Fatura de {{ nome }}");
        var evento = f.NovoEvento();

        await f.Service.AvaliarEventoAsync(evento);

        evento.Status.Should().Be(StatusEventoNotificacao.Processado, evento.ErroProcessamento);
        var msg = f.Gravadas.Should().ContainSingle().Subject;
        msg.Canal.Should().Be(CanalNotificacao.Email);
        msg.Destinatario.Should().Be("maria@example.com");
        msg.AssuntoRenderizado.Should().Be("Fatura de Maria");
    }

    [Fact]
    public async Task ModoTodosGeraUmaMensagemPorCanalPermitido()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email, CanalNotificacao.WhatsApp);
        f.UsarRotina(["Email", "WhatsApp"], parametrosJson: """{"modoCanais":"todos"}""");
        f.UsarTemplate(CanalNotificacao.Email);
        f.UsarTemplate(CanalNotificacao.WhatsApp, metadadosJson: """{"template":"fatura_vencida","param1":"{{ nome }}"}""");

        await f.Service.AvaliarEventoAsync(f.NovoEvento());

        f.Gravadas.Should().HaveCount(2);
        var email = f.Gravadas.Single(m => m.Canal == CanalNotificacao.Email);
        var whats = f.Gravadas.Single(m => m.Canal == CanalNotificacao.WhatsApp);
        email.Destinatario.Should().Be("maria@example.com");
        email.LerMetadados().Should().BeNull();
        whats.Destinatario.Should().Be("+5511999990001");
        whats.LerMetadados()!["template"].Should().Be("fatura_vencida");
        whats.LerMetadados()!["param1"].Should().Be("Maria");
        f.Gravadas.Should().OnlyContain(m => m.CanaisFallbackRestantesJson == "[]");
    }

    [Fact]
    public async Task PayloadCanaisRestringeARotina()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email, CanalNotificacao.WhatsApp);
        f.UsarRotina(["Email", "WhatsApp"], parametrosJson: """{"modoCanais":"todos"}""");
        f.UsarTemplate(CanalNotificacao.Email);
        f.UsarTemplate(CanalNotificacao.WhatsApp);
        var payload = """{"email":"maria@example.com","telefone":"+5511999990001","nome":"Maria","canais":["Email","Sms"]}""";

        await f.Service.AvaliarEventoAsync(f.NovoEvento(payload));

        f.Gravadas.Should().ContainSingle().Which.Canal.Should().Be(CanalNotificacao.Email);
    }

    [Fact]
    public async Task EventoSoFalhaQuandoNenhumCanalTemTemplate()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email, CanalNotificacao.WhatsApp);
        f.UsarRotina(["Email", "WhatsApp"]);
        var evento = f.NovoEvento();

        await f.Service.AvaliarEventoAsync(evento);

        evento.Status.Should().Be(StatusEventoNotificacao.Falhado);
        f.Gravadas.Should().BeEmpty();

        // Com template em apenas um canal, o evento sai por ele e não falha.
        f.UsarTemplate(CanalNotificacao.WhatsApp);
        var outro = f.NovoEvento();
        await f.Service.AvaliarEventoAsync(outro);
        outro.Status.Should().Be(StatusEventoNotificacao.Processado);
        f.Gravadas.Should().ContainSingle().Which.Canal.Should().Be(CanalNotificacao.WhatsApp);
    }

    [Fact]
    public async Task SemChaveDeModoSegueOFallback()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email, CanalNotificacao.WhatsApp);
        f.UsarRotina(["Email", "WhatsApp"]);
        f.UsarTemplate(CanalNotificacao.Email);
        f.UsarTemplate(CanalNotificacao.WhatsApp);

        await f.Service.AvaliarEventoAsync(f.NovoEvento());

        var msg = f.Gravadas.Should().ContainSingle().Subject;
        msg.Canal.Should().Be(CanalNotificacao.Email);
        msg.CanaisFallbackRestantesJson.Should().Contain("WhatsApp");
    }

    [Fact]
    public async Task PausaGeralDaEmpresaSuprimeOEventoMasNaoSeguranca()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        f.Bloqueios.ListarAtivosAsync(f.EmpresaId, Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns([BloqueioNotificacao.Criar("pausa", "admin", f.EmpresaId)]);
        f.UsarTemplate(CanalNotificacao.Email);

        f.UsarRotina(["Email"], CategoriaConteudoNotificacao.Operacional);
        var pausado = f.NovoEvento();
        await f.Service.AvaliarEventoAsync(pausado);
        pausado.Status.Should().Be(StatusEventoNotificacao.Processado);
        f.Gravadas.Should().BeEmpty("a pausa da empresa cala o que não é segurança");

        f.UsarRotina(["Email"], CategoriaConteudoNotificacao.Seguranca);
        await f.Service.AvaliarEventoAsync(f.NovoEvento());
        f.Gravadas.Should().ContainSingle("a pausa da empresa nunca bloqueia Seguranca");
    }

    [Fact]
    public async Task PausaGlobalDoSuperadminCalaTudoInclusiveSeguranca()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        f.Bloqueios.ListarAtivosAsync(null, Arg.Any<CanalNotificacao?>(), Arg.Any<CancellationToken>())
            .Returns([BloqueioNotificacao.Criar("global", "super")]);
        f.UsarTemplate(CanalNotificacao.Email);
        f.UsarRotina(["Email"], CategoriaConteudoNotificacao.Seguranca);

        await f.Service.AvaliarEventoAsync(f.NovoEvento());

        f.Gravadas.Should().BeEmpty();
    }
}
