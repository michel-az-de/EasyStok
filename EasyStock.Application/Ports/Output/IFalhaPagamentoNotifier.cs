namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Port que recebe notificacoes de falha em processamento de pagamento. A
/// implementacao em <c>Api/Services/Faturacao/AutoTicketFalhaPagamento</c>
/// audita cada falha como <c>TipoEventoFatura.PagamentoFalhou</c> e — apos
/// 3 ocorrencias na mesma fatura nos ultimos 7 dias — abre ticket admin
/// categoria=Financeiro com prioridade=Alta vinculado a fatura.
///
/// <para>
/// Idempotente: se a fatura ja tem <c>TicketRelacionadoId</c>, nao duplica.
/// O processor de webhook apenas chama; toda a logica de threshold e
/// integracao com Helpdesk fica encapsulada no adapter.
/// </para>
/// </summary>
public interface IFalhaPagamentoNotifier
{
    /// <summary>
    /// Registra uma falha. <paramref name="faturaId"/> opcional — quando
    /// null (cobranca orfa sem fatura linkada), apenas loga.
    /// </summary>
    Task RegistrarFalhaAsync(
        Guid empresaId,
        Guid? faturaId,
        string motivo,
        CancellationToken ct = default);
}
