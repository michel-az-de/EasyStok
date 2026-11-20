using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;

namespace EasyStock.Application.Tests.Services.Notifications;

/// <summary>#2917: o template do WhatsApp já fecha a frase com ponto; texto livre com ponto final saía com "..".</summary>
public class PublicadorAvisoEquipeTests
{
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();

    private string Campo(string nome)
    {
        var chamada = _notificador.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(INotificadorService.EnfileirarEventoAsync));
        return JsonDocument.Parse((string)chamada.GetArguments()[2]!).RootElement.GetProperty(nome).GetString()!;
    }

    [Theory]
    [InlineData("Novo contato pelo chat do site.", "Novo contato pelo chat do site")]
    [InlineData(" pediu desconto. ", "pediu desconto")]
    [InlineData("pediu para falar com uma pessoa", "pediu para falar com uma pessoa")]
    public async Task MotivoChegaAoTemplateSemPontoFinal(string motivo, string esperado)
    {
        await new PublicadorAvisoEquipe(_notificador).AtencaoNoAtendimentoAsync(Guid.NewGuid(), Guid.NewGuid(), "Teste", motivo);

        Campo("motivo").Should().Be(esperado);
    }

    [Fact]
    public async Task ProblemaDoPagamentoChegaAoTemplateSemPontoFinal()
    {
        await new PublicadorAvisoEquipe(_notificador).ProblemaNoPagamentoAsync(
            Guid.NewGuid(), Guid.NewGuid(), "Teste", "Pagamento recusado.", "chave-1");

        Campo("problema").Should().Be("Pagamento recusado");
    }
}
