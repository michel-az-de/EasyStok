using EasyStock.Application.Ports.Output.Persistence.Campanhas;

namespace EasyStock.Application.Services.Campanhas;

/// <summary>
/// Conversão da campanha (S30, métrica para US-030): pedido de cliente que recebeu campanha
/// <c>Enviando</c> ou <c>Enviada</c> nos últimos 7 dias marca o destinatário como <c>Pediu</c>. Só
/// estagia; quem cria o pedido faz o commit.
/// </summary>
public sealed class AtribuicaoPedidoCampanha(ICampanhaRepository repository, TimeProvider relogio)
{
    public static readonly TimeSpan JanelaAtribuicao = TimeSpan.FromDays(7);

    public async Task AtribuirAsync(Guid empresaId, Guid? clienteId, Guid pedidoId, CancellationToken ct = default)
    {
        if (clienteId is not { } cliente || cliente == Guid.Empty) return;

        var desde = relogio.GetUtcNow().UtcDateTime - JanelaAtribuicao;
        var destinatario = await repository.ObterEnviadoParaAtribuirAsync(empresaId, cliente, desde, ct);
        destinatario?.RegistrarPedido(pedidoId);
    }
}
