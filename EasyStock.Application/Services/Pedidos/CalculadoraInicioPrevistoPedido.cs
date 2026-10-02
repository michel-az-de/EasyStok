using EasyStock.Domain.Sales;

namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// Início previsto do preparo (S21, US-037): <c>início da janela − PrazoMinimo(itens)</c> (S15).
/// A janela é a da vaga ativa (<c>VagaOcupada → JanelaEntrega.HoraInicio</c>, hora de Brasília); sem vaga,
/// vale <see cref="Pedido.AgendadoParaEm"/>; sem nenhum dos dois (pedido para já), não há início previsto.
/// Quando o pedido vira compromisso (pago ou na fila), também recebe o número do dia (S53).
/// </summary>
/// <param name="numerador">Número do dia (S53). Nulo nos testes que só olham o início previsto.</param>
public class CalculadoraInicioPrevistoPedido(IPrazoPreparoPedidoQueries queries, NumeradorPedidoDoDia? numerador = null)
{
    /// <summary>Instante UTC em que o preparo precisa começar, ou nulo sem janela.</summary>
    public async Task<DateTime?> CalcularAsync(Pedido pedido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        return Calcular(pedido, await queries.ObterAsync(pedido.EmpresaId, pedido.Id, ct));
    }

    /// <summary>
    /// O pedido virou compromisso (pagamento confirmado): grava o início previsto e o número do dia. O pedido
    /// precisa estar gravado: a leitura da janela e dos itens vai ao banco.
    /// </summary>
    public async Task AplicarCompromissoAsync(Pedido pedido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var leitura = await queries.ObterAsync(pedido.EmpresaId, pedido.Id, ct);
        pedido.DefinirInicioPrevisto(Calcular(pedido, leitura));
        if (numerador is not null) await numerador.AtribuirAsync(pedido, leitura?.DataJanela, ct: ct);
    }

    /// <summary>
    /// Reagendamento: recalcula o início previsto e, se o dia de produção mudou, troca o número do dia de quem
    /// já tinha. Pedido ainda fora da fila continua sem número.
    /// </summary>
    public async Task RecalcularAsync(Pedido pedido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var leitura = await queries.ObterAsync(pedido.EmpresaId, pedido.Id, ct);
        pedido.DefinirInicioPrevisto(Calcular(pedido, leitura));
        if (numerador is not null) await numerador.AtribuirAsync(pedido, leitura?.DataJanela, somenteSeJaNumerado: true, ct);
    }

    /// <summary>
    /// Ponto único de entrada na fila (#1230): pedido em <see cref="StatusPedido.Aguardando"/> grava o início
    /// previsto e o número do dia; em outro status, nada muda. Todo caminho que coloca o pedido na fila ou registra
    /// o pagamento dele passa por aqui (pagamento na entrega, troca genérica de status, criação no ERP, pagamento
    /// manual); o teste de arquitetura <c>InicioPrevistoNaFilaTests</c> barra caminho novo que esqueça.
    /// </summary>
    public async Task AplicarNaFilaAsync(Pedido pedido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (pedido.StatusEnum != StatusPedido.Aguardando) return;
        await AplicarCompromissoAsync(pedido, ct);
    }

    private static DateTime? Calcular(Pedido pedido, PrazoPreparoPedidoLeitura? leitura)
    {
        if (leitura is null) return null;

        DateTime? entrega = leitura is { DataJanela: { } dia, HoraInicioJanela: { } hora }
            ? HorarioBrasil.InstanteUtc(dia, hora)
            : pedido.AgendadoParaEm;
        if (entrega is null) return null;

        var prazo = CalculadoraPrazoPedido.PrazoMinimo(
            leitura.TemposPreparoMinutos, leitura.TempoPreparoPadraoMinutos, leitura.RespiroMinutos);
        return DateTime.SpecifyKind(entrega.Value, DateTimeKind.Utc).AddMinutes(-prazo);
    }
}
