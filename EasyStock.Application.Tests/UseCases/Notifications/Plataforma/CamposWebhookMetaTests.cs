using EasyStock.Application.UseCases.Notifications.Plataforma;

namespace EasyStock.Application.Tests.UseCases.Notifications.Plataforma;

/// <summary>#1417: os fields da coexistência no callback do app.</summary>
public class CamposWebhookMetaTests
{
    private static string Corpo(params string[] fields) =>
        """{"entry":[{"changes":[""" + string.Join(",", fields.Select(f => $$$"""{"field":"{{{f}}}","value":{"x":1}}""")) + "]}]}";

    [Fact]
    public void EcoDoAppSegueParaOAtendimentoJuntoDasMensagens()
    {
        var campos = CamposWebhookMeta.Separar(Corpo("messages", "smb_message_echoes"));

        campos.Mensagens.Should().Contain("smb_message_echoes").And.Contain("\"messages\"");
        campos.Ignoradas.Should().Be(0);
    }

    [Fact]
    public void HistoricoEEstadoDoAppSaoContadosENaoVaoAoAtendimento()
    {
        var campos = CamposWebhookMeta.Separar(Corpo("history", "smb_app_state_sync", "smb_app_state_sync", "account_update"));

        campos.Mensagens.Should().BeNull();
        campos.Historico.Should().Be(1);
        campos.EstadoApp.Should().Be(2);
        campos.Ignoradas.Should().Be(1, "account_update é aceito e descartado");
    }
}
