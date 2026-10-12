using EasyStock.Application.Ports.Output;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// <c>acao:escolher_janela</c> (S16): o toque no botão de <c>listar_janelas</c> devolve a vez ao agente, que
/// lê a escolha no histórico e fecha o pedido com <c>criar_pedido</c>.
/// </summary>
public class EscolherJanelaAcaoBotaoTests
{
    private static readonly DateTime Agora = new(2026, 6, 2, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnfileiraTurnoDoAgente()
    {
        var empresaId = Guid.NewGuid();
        var queue = Substitute.For<IQueueService>();
        var roteador = new RoteadorAcoesBotao([new EscolherJanelaAcaoBotao(queue)], NullLogger<RoteadorAcoesBotao>.Instance);
        var conversa = Conversa.Abrir(empresaId, "5511999998888", Agora, "Maria");
        conversa.RegistrarEntrada(Agora);

        var tratado = await roteador.ExecutarAsync(
            empresaId, conversa, $"acao:escolher_janela:{Guid.NewGuid()}:2026-06-02", Agora);

        tratado.Should().BeTrue();
        await queue.Received(1).EnqueueAsync(
            FilaAtendimentoNomes.TurnoAgente,
            Arg.Is<ProcessarTurnoAgenteJob>(j => j.EmpresaId == empresaId && j.ConversaId == conversa.Id));
    }
}
