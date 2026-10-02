using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N4: o motor manda a mensagem para cada pessoa da audiência, no contato dela, nunca no do payload.</summary>
public class NotificadorServiceAudienciaTests
{
    private const string Payload = """{"nome":"Equipe","email":"cliente@fora.com","telefone":"+5511911112222"}""";

    private static NotificadorServiceFixture Cenario(out RotinaNotificacao rotina)
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email, CanalNotificacao.WhatsApp);
        rotina = f.UsarRotina(
            ["Email", "WhatsApp"], CategoriaConteudoNotificacao.Operacional,
            """{"modoCanais":"todos","audiencia":"gestores"}""");
        f.UsarTemplate(CanalNotificacao.Email, assunto: "Prazo");
        f.UsarTemplate(CanalNotificacao.WhatsApp, assunto: "", corpo: "Prazo estourado");
        return f;
    }

    [Fact]
    public async Task CadaGestorElegivelRecebeUmaMensagemPorCanalElegivel()
    {
        var f = Cenario(out _);
        var ana = Guid.NewGuid();
        var leo = Guid.NewGuid();
        f.Audiencia.ResolverAsync(default!, default, default, default).ReturnsForAnyArgs(
        [
            new DestinatarioAudiencia(ana, "Ana", "ana@casadababa.com", "+5511997573992", []),
            new DestinatarioAudiencia(leo, "Leo", "leo@casadababa.com", null, [])
        ]);
        var evento = f.NovoEvento(Payload);

        await f.Service.AvaliarEventoAsync(evento);

        evento.Status.Should().Be(StatusEventoNotificacao.Processado, evento.ErroProcessamento);
        f.Gravadas.Should().HaveCount(3);
        f.Gravadas.Where(m => m.UsuarioDestinoId == ana).Select(m => (m.Canal, m.Destinatario)).Should().BeEquivalentTo(
        [
            (CanalNotificacao.Email, "ana@casadababa.com"),
            (CanalNotificacao.WhatsApp, "+5511997573992")
        ]);
        f.Gravadas.Where(m => m.UsuarioDestinoId == leo).Should().ContainSingle()
            .Which.Destinatario.Should().Be("leo@casadababa.com");
        f.Gravadas.Select(m => m.Destinatario).Should().NotContain(["cliente@fora.com", "+5511911112222"]);
    }

    [Fact]
    public async Task ResolveComAEmpresaDoEventoEOUsuarioDoPayload()
    {
        var f = Cenario(out var rotina);
        var ana = Guid.NewGuid();
        f.Audiencia.ResolverAsync(default!, default, default, default).ReturnsForAnyArgs([]);
        var evento = f.NovoEvento($$"""{"nome":"Equipe","usuarioId":"{{ana}}"}""");

        await f.Service.AvaliarEventoAsync(evento);

        await f.Audiencia.Received(1).ResolverAsync(rotina, f.EmpresaId, ana, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AudienciaVaziaFechaOEventoSemMensagem()
    {
        var f = Cenario(out _);
        f.Audiencia.ResolverAsync(default!, default, default, default).ReturnsForAnyArgs([]);
        var evento = f.NovoEvento(Payload);

        await f.Service.AvaliarEventoAsync(evento);

        evento.Status.Should().Be(StatusEventoNotificacao.Processado);
        f.Gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task AudienciaNulaMantemOContatoDoPayload()
    {
        var f = Cenario(out _);
        var evento = f.NovoEvento(Payload);

        await f.Service.AvaliarEventoAsync(evento);

        f.Gravadas.Select(m => (m.Canal, m.Destinatario)).Should().BeEquivalentTo(
        [
            (CanalNotificacao.Email, "cliente@fora.com"),
            (CanalNotificacao.WhatsApp, "+5511911112222")
        ]);
        f.Gravadas.Should().OnlyContain(m => m.UsuarioDestinoId == null);
    }

    [Fact]
    public async Task PessoaSemContatoElegivelNaoGeraMensagemMasOutraGera()
    {
        var f = Cenario(out _);
        f.Audiencia.ResolverAsync(default!, default, default, default).ReturnsForAnyArgs(
        [
            new DestinatarioAudiencia(Guid.NewGuid(), "Sem contato", null, null, []),
            new DestinatarioAudiencia(Guid.NewGuid(), "Leo", "leo@casadababa.com", null, [])
        ]);
        var evento = f.NovoEvento(Payload);

        await f.Service.AvaliarEventoAsync(evento);

        evento.Status.Should().Be(StatusEventoNotificacao.Processado);
        f.Gravadas.Should().ContainSingle().Which.Destinatario.Should().Be("leo@casadababa.com");
    }

    [Fact]
    public async Task NinguemComContatoMarcaOEventoComoFalhado()
    {
        var f = Cenario(out _);
        f.Audiencia.ResolverAsync(default!, default, default, default).ReturnsForAnyArgs(
            [new DestinatarioAudiencia(Guid.NewGuid(), "Sem contato", null, null, [])]);
        var evento = f.NovoEvento(Payload);

        await f.Service.AvaliarEventoAsync(evento);

        evento.Status.Should().Be(StatusEventoNotificacao.Falhado);
        f.Gravadas.Should().BeEmpty();
    }
}
