namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// Número do dia do pedido (S53), para falar na cozinha ("042"). O dia é o de produção, igual ao do KDS: a data
/// da vaga ativa; sem vaga, a do agendamento; sem agendamento, hoje (Brasília). Assim dois pedidos do mesmo dia
/// de produção nunca dividem o número, mesmo pagos em dias diferentes. Depois de atribuído, o número não muda
/// (#1641-3); só o reagendamento explícito (troca de janela ou de dia, #1293) dá número novo no dia novo.
/// </summary>
public sealed class NumeradorPedidoDoDia(ISequenciaDiariaPedido sequencia, TimeProvider relogio)
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

        pedido.DefinirNumeroDoDia(dia, await sequencia.ProximoAsync(pedido.EmpresaId, dia, ct));
    }

    public static DateOnly DiaDeProducao(Pedido pedido, DateOnly? dataJanela, DateTime agoraUtc) =>
        dataJanela ?? HorarioBrasil.DataOperacional(pedido.AgendadoParaEm ?? agoraUtc);
}
