namespace EasyStock.Domain.Sales;

/// <summary>
/// Status do agregado <see cref="Entities.Pedido"/>. Ordem reflete o ciclo
/// de vida atual do ERP (Casa da Babá em produção, mobile e PWA).
/// Valores numéricos explícitos pra estabilidade em serialização e
/// persistência futura — adicionar novo estado nunca renumera os atuais.
///
/// <para>
/// Estados adicionais que dão suporte às integrações (PagamentoAutorizado,
/// EmTransito, Fechado, DevolucaoSolicitada, etc.) entram em fases
/// posteriores quando os bounded contexts correspondentes (Payments,
/// Logistics, Fiscal) forem implementados. Esta enum cobre apenas o que
/// já existe em produção.
/// </para>
/// </summary>
public enum StatusPedido
{
    /// <summary>Pedido criado, aguardando confirmação/início de preparo.</summary>
    Aguardando = 1,

    /// <summary>Em preparo (cozinha, montagem, separação).</summary>
    Preparando = 2,

    /// <summary>Pronto pra entrega/retirada. Estoque baixado a partir desse ponto.</summary>
    Pronto = 3,

    /// <summary>Entregue ao cliente. Estoque já estava baixado.</summary>
    Entregue = 4,

    /// <summary>Cancelado. Se estava com estoque baixado, é devolvido na transição.</summary>
    Cancelado = 5,

    // ── Estados do fluxo Storefront (ADR-0014) ────────────────────────

    /// <summary>
    /// Pedido criado em Fase 1 do checkout — reserva de vaga não ocorreu ainda.
    /// Status transitório (máx. ~30 s até avançar para AguardandoPagamento ou Cancelado).
    /// </summary>
    Rascunho = 0,

    /// <summary>
    /// Vaga reservada (Fase 2 OK), aguardando confirmação de pagamento via webhook MP.
    /// Background service CancelarPedidosAbandonados cancela após 30 min sem confirmação.
    /// </summary>
    AguardandoPagamento = 6,

    /// <summary>
    /// Pagamento aprovado pelo MP (via webhook). Aguarda decisão manual da babá
    /// para começar a preparar (TASK-EZ-APROVAR-001). ADR-0006 §Process aprovado.
    /// </summary>
    AguardandoAprovacaoBaba = 7,
}
