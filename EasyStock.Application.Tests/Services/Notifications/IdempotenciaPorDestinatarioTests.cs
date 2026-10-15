using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>N4: a chave de negócio do outbox passa a distinguir o destinatário da audiência, sem mexer na do cliente final.</summary>
public class IdempotenciaPorDestinatarioTests
{
    private const string Chave = "prazo:AB12CD34|estourado";

    [Fact]
    public void ChaveDeNegocioMudaComODestinatario()
    {
        var ana = Guid.NewGuid().ToString("N");
        var leo = Guid.NewGuid().ToString("N");

        var deAna = OutboxMensagemNotificacao.ComputarIdempotencyKey(Chave, CanalNotificacao.Email, ana);
        var deLeo = OutboxMensagemNotificacao.ComputarIdempotencyKey(Chave, CanalNotificacao.Email, leo);

        deAna.Should().NotBe(deLeo);
        deAna.Should().Be(OutboxMensagemNotificacao.ComputarIdempotencyKey(Chave, CanalNotificacao.Email, ana), "é determinística");
        deAna.Should().NotBe(OutboxMensagemNotificacao.ComputarIdempotencyKey(Chave, CanalNotificacao.Email));
    }

    [Fact]
    public void ChaveSemDestinatarioNaoMudaParaOCliente()
    {
        // O hash de hoje: o que já está no outbox do cliente final continua deduplicando.
        var esperado = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"negocio|{Chave}|{(int)CanalNotificacao.Email}")));

        OutboxMensagemNotificacao.ComputarIdempotencyKey(Chave, CanalNotificacao.Email).Should().Be(esperado);
        OutboxMensagemNotificacao.ComputarIdempotencyKey(Chave, CanalNotificacao.Email, null).Should().Be(esperado);
        OutboxMensagemNotificacao.ComputarIdempotencyKey(Chave, CanalNotificacao.Email, "  ").Should().Be(esperado);
    }

    [Fact]
    public async Task DoisAdminsComAMesmaChaveDeNegocioGeramDuasMensagens()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        var rotina = f.UsarRotina(
            ["Email"], CategoriaConteudoNotificacao.Operacional, """{"modoCanais":"todos","audiencia":"admins"}""");
        f.UsarTemplate(CanalNotificacao.Email, assunto: "Prazo {{ nome }}");
        var ana = Guid.NewGuid();
        var leo = Guid.NewGuid();
        f.Audiencia.ResolverAsync(rotina, f.EmpresaId, null, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(
        [
            new DestinatarioAudiencia(ana, "Ana", "ana@casadababa.com", null, []),
            new DestinatarioAudiencia(leo, "Leo", "leo@casadababa.com", null, [])
        ]);
        var payload = """{"nome":"Equipe","chaveIdempotencia":"prazo:AB12CD34|estourado"}""";

        await f.Service.AvaliarEventoAsync(f.NovoEvento(payload));

        f.Gravadas.Should().HaveCount(2);
        f.Gravadas.Select(m => m.Destinatario).Should().BeEquivalentTo("ana@casadababa.com", "leo@casadababa.com");
        f.Gravadas.Select(m => m.UsuarioDestinoId).Should().BeEquivalentTo([ana, leo]);
        f.Gravadas.Select(m => m.IdempotencyKey).Distinct().Should().HaveCount(2);

        // O mesmo fato reprocessado não duplica ninguém.
        var segundo = f.NovoEvento(payload);
        await f.Service.AvaliarEventoAsync(segundo);
        f.Gravadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task ChaveDoClienteFinalNaoMudaSemAudiencia()
    {
        var f = new NotificadorServiceFixture(CanalNotificacao.Email);
        f.UsarRotina(["Email"]);
        f.UsarTemplate(CanalNotificacao.Email);
        var payload = """{"email":"maria@example.com","nome":"Maria","chaveIdempotencia":"pedido:1|pago"}""";

        await f.Service.AvaliarEventoAsync(f.NovoEvento(payload));

        f.Gravadas.Should().ContainSingle().Which.IdempotencyKey.Should().Be(
            OutboxMensagemNotificacao.ComputarIdempotencyKey("pedido:1|pago", CanalNotificacao.Email));
    }
}
