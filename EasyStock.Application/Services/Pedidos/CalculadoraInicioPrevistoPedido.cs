using EasyStock.Domain.Sales;

namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// Início previsto do preparo (S21, US-037): <c>início da janela − PrazoMinimo(itens)</c> (S15).
/// A janela é a da vaga ativa (<c>VagaOcupada → JanelaEntrega.HoraInicio</c>, hora de Brasília); sem vaga,
/// vale <see cref="Pedido.AgendadoParaEm"/>; sem nenhum dos dois (pedido para já), não há início previsto.
/// </summary>
public class CalculadoraInicioPrevistoPedido(IPrazoPreparoPedidoQueries queries)
{
    /// <summary>Instante UTC em que o preparo precisa começar, ou nulo sem janela.</summary>
    public async Task<DateTime?> CalcularAsync(Pedido pedido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        var leitura = await queries.ObterAsync(pedido.EmpresaId, pedido.Id, ct);
        if (leitura is null) return null;

        DateTime? entrega = leitura is { DataJanela: { } dia, HoraInicioJanela: { } hora }
            ? HorarioBrasil.InstanteUtc(dia, hora)
            : pedido.AgendadoParaEm;
        if (entrega is null) return null;

        var prazo = CalculadoraPrazoPedido.PrazoMinimo(
            leitura.TemposPreparoMinutos, leitura.TempoPreparoPadraoMinutos, leitura.RespiroMinutos);
        return DateTime.SpecifyKind(entrega.Value, DateTimeKind.Utc).AddMinutes(-prazo);
    }

    /// <summary>
    /// Ponto único de entrada na fila (#1230): pedido em <see cref="StatusPedido.Aguardando"/> grava o início
    /// previsto; em outro status, nada muda. Todo caminho que coloca o pedido na fila ou registra o pagamento
    /// dele passa por aqui (pagamento na entrega, troca genérica de status, criação no ERP, pagamento manual);
    /// o teste de arquitetura <c>InicioPrevistoNaFilaTests</c> barra caminho novo que esqueça.
    /// O pedido precisa estar gravado: a leitura da janela e dos itens vai ao banco.
    /// </summary>
    public async Task AplicarNaFilaAsync(Pedido pedido, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (pedido.StatusEnum != StatusPedido.Aguardando) return;
        pedido.DefinirInicioPrevisto(await CalcularAsync(pedido, ct));
    }
}
