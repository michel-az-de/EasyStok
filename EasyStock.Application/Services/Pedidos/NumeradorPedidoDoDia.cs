namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// Número do dia do pedido (S53), para falar na cozinha ("042"). O dia é o de produção, igual ao do KDS: a data
/// da vaga ativa; sem vaga, a do agendamento; sem agendamento, hoje (Brasília). Assim dois pedidos do mesmo dia
/// de produção nunca dividem o número, mesmo pagos em dias diferentes. Depois de atribuído, o número não muda
/// (#1641-3); só o reagendamento explícito (troca de janela ou de dia, #1293) dá número novo no dia novo.
/// </summary>
/// <para>E12 (#1641-4, decisão do Felipe em 23/10): a falha do contador nunca desfaz o pagamento. O pedido fica sem
/// número (buraco na sequência é aceitável) e a próxima entrada na fila ou no preparo tenta de novo.</para>
public sealed class NumeradorPedidoDoDia(ISequenciaDiariaPedido sequencia, TimeProvider relogio,
    ILogger<NumeradorPedidoDoDia>? logger = null)
{
    /// <param name="dataJanela">Data da vaga ativa, quando houver (<see cref="PrazoPreparoPedidoLeitura.DataJanela"/>).</param>
    /// <param name="reagendamento">Troca de janela ou de dia: troca o número só de quem já tinha e mudou de dia de
    /// produção; não numera pedido que ainda não entrou na fila. Fora dele, numera só quem ainda não tem número.</param>
    public async Task AtribuirAsync(Pedido pedido, DateOnly? dataJanela, bool reagendamento = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (reagendamento ? pedido.NumeroDoDia is null : pedido.NumeroDoDia is not null) return;

        var dia = DiaDeProducao(pedido, dataJanela, relogio.GetUtcNow().UtcDateTime);
        if (pedido.TemNumeroDoDia(dia)) return;

        int numero;
        try
        {
            numero = await sequencia.ProximoAsync(pedido.EmpresaId, dia, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Numero do dia nao atribuido pedidoId={PedidoId} dia={Dia}", pedido.Id, dia);
            return;
        }
        pedido.DefinirNumeroDoDia(dia, numero);
    }

    public static DateOnly DiaDeProducao(Pedido pedido, DateOnly? dataJanela, DateTime agoraUtc) =>
        dataJanela ?? HorarioBrasil.DataOperacional(pedido.AgendadoParaEm ?? agoraUtc);
}
