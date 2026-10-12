using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Reenvio;

/// <summary>S59 (#1391): o painel "Não entregues" lista a mensagem com o contato e limita a página.</summary>
public class ListarNaoEntreguesUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();

    [Fact]
    public async Task DevolveMensagemComOContato()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511982254398", Agora, "Maria");
        var m = Mensagem.Saida(_empresaId, conversa.Id, AutorMensagem.Agente, Agora, TipoConteudoMensagem.Texto, "Oi");
        m.RegistrarFalhaEnvio("Meta fora", TipoFalhaEnvio.Permanente, Agora);
        _conversas.ListarNaoEntreguesAsync(_empresaId, 50, Arg.Any<CancellationToken>()).Returns([new MensagemNaoEntregue(conversa, m)]);

        var r = await new ListarNaoEntreguesUseCase(_conversas).ExecuteAsync(_empresaId, null);

        var linha = r.Should().ContainSingle().Subject;
        linha.ConversaId.Should().Be(conversa.Id);
        linha.ContatoNome.Should().Be("Maria");
        linha.ConversaAberta.Should().BeTrue();
        linha.Mensagem.Status.Should().Be(StatusMensagem.Falhou);
        linha.Mensagem.Erro.Should().Be("Meta fora");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5000, ListarNaoEntreguesUseCase.LimiteMaximo)]
    public async Task LimitaAPagina(int pedido, int usado)
    {
        await new ListarNaoEntreguesUseCase(_conversas).ExecuteAsync(_empresaId, pedido);

        await _conversas.Received(1).ListarNaoEntreguesAsync(_empresaId, usado, Arg.Any<CancellationToken>());
    }
}
