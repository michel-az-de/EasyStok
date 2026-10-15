using EasyStock.Infra.Notifications.Push;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>#1508: o endpoint do Web Push só pode apontar para serviço de push conhecido (sem SSRF).</summary>
public class EndpointPushPermitidoTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc:def")]
    [InlineData("https://web.push.apple.com/QGx3abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAAA")]
    [InlineData("https://push.services.mozilla.com.x.push.services.mozilla.com/wpush/v2/a")]
    [InlineData("https://wns2-by3p.notify.windows.com/w/?token=BQYAAA")]
    [InlineData("https://FCM.googleapis.com/fcm/send/abc")]
    public void Aceita_servicos_de_push_conhecidos(string endpoint) =>
        EndpointPushPermitido.Valido(endpoint).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-url")]
    [InlineData("/relativo/fcm")]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")]
    [InlineData("https://user:senha@fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://localhost/push")]
    [InlineData("https://fcm.googleapis.com.evil.example/fcm/send/abc")]
    [InlineData("https://evilpush.apple.com/x")]
    [InlineData("https://apple.com/x")]
    [InlineData("ftp://fcm.googleapis.com/fcm/send/abc")]
    public void Recusa_o_resto(string? endpoint) =>
        EndpointPushPermitido.Valido(endpoint).Should().BeFalse();

    [Fact]
    public void Recusa_endpoint_maior_que_a_coluna() =>
        EndpointPushPermitido.Valido("https://fcm.googleapis.com/fcm/send/" + new string('a', 2000))
            .Should().BeFalse();
}
