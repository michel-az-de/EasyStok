namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Pedido parado em <c>AguardandoPagamento</c> (S43).</summary>
public sealed record PedidoSemBaixa(Guid EmpresaId, Guid PedidoId, string? ClienteNome, Guid? ConversaId);

/// <summary>
/// Conversa assumida cuja última mensagem que conta (nota interna do sistema não conta) é do
/// cliente (S43). <see cref="MensagemEntradaId"/> é a referência do lembrete: se o cliente escrever de
/// novo depois de respondido, é outro fato e outro lembrete.
/// </summary>
public sealed record ConversaSemResposta(
    Guid EmpresaId, Guid ConversaId, Guid MensagemEntradaId, string? ContatoNome, Guid? AssumidaPorUsuarioId);

/// <summary>
/// Consultas cross-tenant que alimentam o avaliador de lembretes (S43). Rodam com bypass de RLS
/// ligado pelo host. Trazem tudo o que atende à condição, sem página: a resolução sozinha do
/// avaliador depende de a lista estar completa.
/// </summary>
public interface ICandidatosLembreteQuery
{
    /// <summary>Pedidos em <c>AguardandoPagamento</c> sem alteração desde <paramref name="paradoDesdeAntesDe"/>.</summary>
    Task<IReadOnlyList<PedidoSemBaixa>> ListarPedidosSemBaixaAsync(DateTime paradoDesdeAntesDe, CancellationToken ct = default);

    /// <summary>Conversas assumidas e abertas com o cliente esperando desde antes de <paramref name="entradaAntesDe"/>.</summary>
    Task<IReadOnlyList<ConversaSemResposta>> ListarConversasSemRespostaAsync(DateTime entradaAntesDe, CancellationToken ct = default);
}
