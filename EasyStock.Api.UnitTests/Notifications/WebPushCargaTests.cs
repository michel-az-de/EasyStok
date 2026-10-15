using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Push;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #1807: a carga do Web Push usa a tag dos metadados quando o template a define, para o aviso do servidor
/// substituir a notificação do console (mesma tag) em vez de aparecer duas vezes.
/// </summary>
public class WebPushCargaTests
{
    private static MensagemPronta Mensagem(IReadOnlyDictionary<string, string>? metadados) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "empresa:x", "Contato novo", "Maria entrou em contato",
            CanalNotificacao.Push, CategoriaConteudoNotificacao.Operacional) { Metadados = metadados };

    [Fact]
    public void TagDosMetadadosVaiNaCarga()
    {
        var carga = JsonDocument.Parse(WebPushCanal.MontarCarga(Mensagem(new Dictionary<string, string>
        {
            ["tag"] = "casa-da-baba-contato-novo-abc",
        }))).RootElement;

        carga.GetProperty("tag").GetString().Should().Be("casa-da-baba-contato-novo-abc");
        carga.GetProperty("title").GetString().Should().Be("Contato novo");
        carga.GetProperty("body").GetString().Should().Be("Maria entrou em contato");
    }

    [Fact]
    public void SemTagNosMetadadosUsaOIdDoOutbox()
    {
        var mensagem = Mensagem(null);

        var carga = JsonDocument.Parse(WebPushCanal.MontarCarga(mensagem)).RootElement;

        carga.GetProperty("tag").GetString().Should().Be(mensagem.OutboxId.ToString());
    }
}
