namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// Número do dia do pedido (S53), para falar na cozinha ("042"). O dia é o de produção, igual ao do KDS: a data
/// da vaga ativa; sem vaga, a do agendamento; sem agendamento, hoje (Brasília). Assim dois pedidos do mesmo dia
/// de produção nunca dividem o número, mesmo pagos em dias diferentes.
/// </summary>
public sealed class NumeradorPedidoDoDia(ISequenciaDiariaPedido sequencia, TimeProvider relogio)
{
    /// <param name="dataJanela">Data da vaga ativa, quando houver (<see cref="PrazoPreparoPedidoLeitura.DataJanela"/>).</param>
    /// <param name="somenteSeJaNumerado">Reagendamento: troca o número só de quem já tinha (dia novo); não numera
    /// pedido que ainda não entrou na fila.</param>
    public async Task AtribuirAsync(Pedido pedido, DateOnly? dataJanela, bool somenteSeJaNumerado = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (somenteSeJaNumerado && pedido.NumeroDoDia is null) return;

        var dia = DiaDeProducao(pedido, dataJanela, relogio.GetUtcNow().UtcDateTime);
        if (pedido.TemNumeroDoDia(dia)) return;

        pedido.DefinirNumeroDoDia(dia, await sequencia.ProximoAsync(pedido.EmpresaId, dia, ct));
    }

    public static DateOnly DiaDeProducao(Pedido pedido, DateOnly? dataJanela, DateTime agoraUtc) =>
        dataJanela ?? HorarioBrasil.DataOperacional(pedido.AgendadoParaEm ?? agoraUtc);
}
