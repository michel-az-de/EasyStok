using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

public class ListarConversasAtendimentoUseCaseTests
{
    [Theory]
    [InlineData(null, 7)]
    [InlineData(2, 2)]
    public async Task InboxUsaMesmoPrazoDoAvaliadorEFiltraOutraEmpresa(int? configurado, int esperado)
    {
        var empresa = Guid.NewGuid();
        var repo = Substitute.For<IConversaRepository>();
        var config = Substitute.For<IConfiguracaoAtendimentoRepository>();
        var conversa = Conversa.Abrir(empresa, "5511999990001", DateTime.UtcNow);
        var alheia = Conversa.Abrir(Guid.NewGuid(), "5511999990002", DateTime.UtcNow);
        repo.ListarInboxAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs([new ConversaInboxItem(conversa, null, true), new ConversaInboxItem(alheia, null, true)]);
        if (configurado is { } minutos)
        {
            var salvo = ConfiguracaoAtendimento.CriarPadrao(empresa);
            salvo.SlaRespostaMinutos = minutos;
            config.GetByEmpresaIdAsync(empresa).Returns(salvo);
        }
        var caso = new ListarConversasAtendimentoUseCase(repo, config, Options.Create(new PrazosOptions { ClienteSemRespostaMin = 7 }));
        var resultado = await caso.ExecuteAsync(new ListarConversasAtendimentoQuery(empresa, null, null, 1, 10));
        var resumo = resultado.Should().ContainSingle().Subject;
        resumo.Id.Should().Be(conversa.Id);
        resumo.SlaRespostaMinutos.Should().Be(esperado);
        resumo.AguardaResposta.Should().BeTrue();
    }
}
